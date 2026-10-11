using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using GameMode.Core;
using Microsoft.Win32.SafeHandles;

namespace GameMode.Windows.Processes;

/// <summary>Conservative Windows implementation. Discovery is read-only; mutation always rechecks the held process handle.</summary>
public sealed class WindowsProcessPlatform : IProcessPlatform
{
    private readonly ProcessSafety safety = new();
    public static string ResolveExecutablePath(string path) => ProcessSafety.ResolveExecutablePath(path);

    public Task<ProcessScan> ScanAsync(CancellationToken cancellationToken) => Task.Run(() => Scan(cancellationToken), cancellationToken);

    private ProcessScan Scan(CancellationToken cancellationToken)
    {
        try
        {
            var parents = ReadProcessTable();
            var windows = ReadWindows();
            var inventory = safety.ReadInventory(parents);
            var result = new List<ProcessInfo>(parents.Count);
            foreach (var pair in parents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var processId = pair.Key;
                var name = pair.Value.Name;
                var visible = windows.TryGetValue(processId, out var processWindows) && processWindows.Count > 0;
                using var handle = NativeMethods.OpenProcess(NativeMethods.QueryLimited | NativeMethods.Synchronize, false, processId);
                if (handle.IsInvalid)
                {
                    result.Add(new(processId, 0, pair.Value.Parent, name, null, 0, false,
                        "Нет доступа к сведениям процесса; завершение запрещено", visible, -1));
                    continue;
                }
                long start = 0, workingSet = 0;
                var session = -1;
                string? path = null, reason = null;
                try
                {
                    start = ReadStart(handle);
                    path = ReadPath(handle);
                    name = Path.GetFileName(path);
                    if (!NativeMethods.ProcessIdToSessionId(processId, out var sessionValue)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Недоступен сеанс процесса");
                    session = checked((int)sessionValue);
                    var counters = new NativeMethods.MemoryCounters { Size = (uint)Marshal.SizeOf<NativeMethods.MemoryCounters>() };
                    if (!NativeMethods.GetProcessMemoryInfo(handle, out counters, (uint)Marshal.SizeOf<NativeMethods.MemoryCounters>()))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Недоступен Working Set");
                    workingSet = checked((long)counters.WorkingSetSize);
                    reason = safety.ProtectionReason(handle, processId, session, path, visible, inventory);
                }
                catch (Exception error) { reason = $"Не все сведения безопасности доступны: {error.Message}"; }
                result.Add(new(processId, start, pair.Value.Parent, name, path, workingSet,
                    reason is null && inventory.Available, reason, visible, session));
            }
            return new(result, inventory.Available, inventory.Error);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return new([], false, $"Сканирование процессов не завершено: {error.Message}. Закрытие запрещено."); }
    }

    public Task<RestartCapture> CaptureRestartAsync(ProcessInfo process, CancellationToken cancellationToken) =>
        Task.Run(() => CaptureRestart(process, cancellationToken), cancellationToken);

    private RestartCapture CaptureRestart(ProcessInfo process, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scan = Scan(cancellationToken);
            var current = FindSame(scan, process);
            if (!scan.SafetyInventoryAvailable || current?.SafetyVerified != true || current.FullPath is null)
                return new(null, current?.ProtectedReason ?? scan.Error ?? "Процесс изменился или уже завершился.");
            var reason = safety.RestartExecutableReason(current.FullPath);
            if (reason is not null) return new(null, reason);
            using var handle = NativeMethods.OpenProcess(NativeMethods.QueryInformation | NativeMethods.QueryLimited | NativeMethods.VmRead | NativeMethods.Synchronize, false, process.Id);
            if (handle.IsInvalid) return new(null, "Командная строка и рабочая папка недоступны без дополнительных прав; возврат отключён.");
            if (!Matches(handle, current)) return new(null, "Идентификатор процесса изменился до сохранения данных запуска.");
            if (!NativeMethods.IsWow64Process2(handle, out var processMachine, out var nativeMachine) || processMachine != 0 || nativeMachine != 0x8664 || IntPtr.Size != 8)
                return new(null, "Безопасное чтение рабочей папки поддерживается только для собственных x64-процессов Windows x64.");
            var (image, commandLine, workingDirectory) = ReadLaunchParameters(handle);
            if (!string.Equals(ProcessSafety.ResolveExecutablePath(image), current.FullPath, StringComparison.OrdinalIgnoreCase))
                return new(null, "Путь в параметрах запуска отличается от фактического исполняемого файла.");
            if (!TryExtractArguments(commandLine, current.FullPath, out var arguments, out var argumentsReason))
                return new(null, argumentsReason);
            if (!Path.IsPathFullyQualified(workingDirectory) || !Directory.Exists(workingDirectory) ||
                NativeMethods.GetDriveType(Path.GetPathRoot(workingDirectory)!) != 3)
                return new(null, "Исходная рабочая папка недоступна или не является локальной; возврат отключён.");
            var windows = ReadWindows().GetValueOrDefault(process.Id, []);
            if (windows.Count == 0) return new(null, "Видимое окно исчезло; безопасный возврат фонового процесса запрещён.");
            var details = windows.Select(window => ReadWindowInfo(window)).ToList();
            var hash = HashFile(current.FullPath);
            if (!Matches(handle, current)) return new(null, "Процесс изменился во время сохранения данных запуска.");
            // PEB is mutable. A second read prevents storing a torn or changing set of launch parameters.
            if (ReadLaunchParameters(handle) != (image, commandLine, workingDirectory))
                return new(null, "Параметры запуска изменились во время чтения; возврат отключён.");
            return new(new(current.FullPath, arguments, workingDirectory, details, hash), null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return new(null, $"Не удалось сохранить безопасные данные повторного запуска: {error.Message}"); }
    }

    public async Task<CloseResult> CloseAsync(ProcessInfo process, bool allowForce,
        Func<ProcessScan, ProcessInfo, bool> authorize, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Keep this handle alive throughout WM_CLOSE and termination, preventing a reused PID from becoming the target.
            using var handle = OpenCloseHandle(process.Id, allowForce, out var canTerminate);
            if (handle.IsInvalid) return new(false, false, "Windows отказала в доступе. Возможно, нужны повышенные права; автоматического повышения нет.");
            if (!Matches(handle, process)) return new(false, false, "Процесс уже завершился или его идентификатор изменился.");
            var scan = await ScanAsync(cancellationToken).ConfigureAwait(false);
            var current = FindSame(scan, process);
            if (!CanAct(scan, current, handle, authorize)) return new(false, false, "Повторная проверка защиты или правил запретила закрытие.");
            var windows = ReadWindows().GetValueOrDefault(process.Id, []);
            if (windows.Count == 0) return new(false, false, "Нет видимых окон для безопасного обычного закрытия.");
            var requested = false;
            foreach (var window in windows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NativeMethods.GetWindowThreadProcessId(window, out var owner);
                if (owner != process.Id || !Matches(handle, process) || !authorize(scan, current!)) continue;
                requested |= NativeMethods.PostMessage(window, 0x0010, 0, 0); // WM_CLOSE does not bypass the app's save prompts.
            }
            if (!requested) return new(false, false, "Windows не разрешила отправить запрос обычного закрытия окна.");
            var end = Environment.TickCount64 + 8000;
            while (Environment.TickCount64 < end)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (NativeMethods.WaitForSingleObject(handle, 0) == NativeMethods.WaitObject0)
                    return new(true, false, "Приложение закрылось после обычного запроса (WM_CLOSE).");
                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            }
            if (!allowForce) return new(false, false, "Приложение не закрылось за 8 секунд; принудительное завершение отключено.");
            if (!canTerminate) return new(false, false, "Обычное закрытие запрошено; Windows не предоставила право принудительного завершения. Повышение прав не выполнялось.");
            scan = await ScanAsync(cancellationToken).ConfigureAwait(false);
            current = FindSame(scan, process);
            if (!CanAct(scan, current, handle, authorize)) return new(false, false, "Перед принудительным завершением защита или правила изменились; действие отменено.");
            cancellationToken.ThrowIfCancellationRequested();
            if (!NativeMethods.TerminateProcess(handle, 0))
                return new(false, false, $"Windows отказала в завершении: {new Win32Exception(Marshal.GetLastWin32Error()).Message}. Повышение прав не выполнялось.");
            for (var i = 0; i < 20; i++)
            {
                if (NativeMethods.WaitForSingleObject(handle, 0) == NativeMethods.WaitObject0)
                    return new(true, true, "Принудительно завершён только этот процесс после 8 секунд ожидания; несохранённые данные могли быть потеряны.");
                await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
            }
            return new(false, true, "Windows приняла команду завершения, но выход процесса ещё не подтверждён; повторный запуск не разрешён.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return new(false, false, $"Закрытие отменено: {error.Message}"); }
    }

    public async Task<LaunchResult> RestartAsync(LaunchInfo launch, Func<ProcessScan, bool> authorize, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ProcessSafety.ResolveExecutablePath(launch.FullPath);
            if (!string.Equals(path, launch.FullPath, StringComparison.OrdinalIgnoreCase)) return new(false, "Фактический путь .exe изменился; возврат запрещён.");
            var parents = await Task.Run(ReadProcessTable, cancellationToken).ConfigureAwait(false);
            var inventory = await Task.Run(() => safety.ReadInventory(parents), cancellationToken).ConfigureAwait(false);
            if (!inventory.Available) return new(false, inventory.Error ?? "Защитный перечень недоступен.");
            var reason = safety.RestartExecutableReason(path, inventory.AntivirusDirectories);
            if (reason is not null) return new(false, reason);
            if (UnsafeArguments(launch.Arguments) is { } argumentReason) return new(false, argumentReason);
            if (launch.Windows.Count == 0) return new(false, "Нет сохранённых сведений об окнах; возврат запрещён.");
            if (!Path.IsPathFullyQualified(launch.WorkingDirectory) || !Directory.Exists(launch.WorkingDirectory) ||
                NativeMethods.GetDriveType(Path.GetPathRoot(launch.WorkingDirectory)!) != 3)
                return new(false, "Исходная рабочая папка недоступна; она не будет заменена другой.");
            if (!string.Equals(HashFile(path), launch.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
                return new(false, "Исполняемый файл изменился после закрытия (SHA-256); возврат запрещён.");
            // A process with an unreadable path might be another instance of this application: fail closed.
            var duplicates = CheckDuplicates(path);
            if (duplicates is not null) return new(false, duplicates);
            cancellationToken.ThrowIfCancellationRequested();
            // Keep the image locked against writes/deletion between final hash verification and CreateProcess.
            using var imageLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var lockedHash = Convert.ToHexString(SHA256.HashData(imageLock));
            if (!string.Equals(lockedHash, launch.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
                return new(false, "Исполняемый файл изменился перед запуском; возврат запрещён.");
            var finalScan = await ScanAsync(cancellationToken).ConfigureAwait(false);
            if (!finalScan.SafetyInventoryAvailable || !authorize(finalScan))
                return new(false, "Перед запуском изменились настройки, исключения, список игр или проверка безопасности; возврат отменён.");
            if (CheckDuplicates(path) is { } finalDuplicate) return new(false, finalDuplicate);
            cancellationToken.ThrowIfCancellationRequested();
            var startup = new NativeMethods.StartupInfo { Size = (uint)Marshal.SizeOf<NativeMethods.StartupInfo>(), Flags = 1, ShowWindow = 7 }; // SW_SHOWMINNOACTIVE
            var command = new StringBuilder($"\"{path}\"" + (launch.Arguments.Length == 0 ? "" : " " + launch.Arguments));
            if (command.Length >= 32767) return new(false, "Командная строка слишком длинная для безопасного запуска.");
            if (!NativeMethods.CreateProcess(path, command, 0, 0, false, 0, 0, launch.WorkingDirectory, ref startup, out var created))
                return new(false, $"Не удалось повторно запустить приложение: {new Win32Exception(Marshal.GetLastWin32Error()).Message}. Автоматического повышения прав нет.");
            try
            {
                using var createdHandle = new SafeProcessHandle(created.Process, false);
                // Some apps ignore STARTUPINFO. Minimize only this still-live PID's windows for a short bounded period.
                for (var i = 0; i < 15; i++)
                {
                    if (NativeMethods.WaitForSingleObject(createdHandle, 0) == NativeMethods.WaitObject0) break;
                    var windows = ReadWindows().GetValueOrDefault(checked((int)created.ProcessId), []);
                    foreach (var window in windows)
                    {
                        NativeMethods.GetWindowThreadProcessId(window, out var owner);
                        if (owner == created.ProcessId) NativeMethods.ShowWindowAsync(window, 7);
                    }
                    await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally { NativeMethods.CloseHandle(created.Thread); NativeMethods.CloseHandle(created.Process); }
            return new(true, "Приложение запущено заново с запросом свёрнутого окна. Несохранённые данные и прежнее состояние не восстанавливаются; отдельные приложения могут игнорировать сворачивание.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return new(false, $"Повторный запуск отменён: {error.Message}"); }
    }

    private static Dictionary<int, (int Parent, string Name)> ReadProcessTable()
    {
        using var snapshot = NativeMethods.CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Снимок процессов недоступен");
        var table = new Dictionary<int, (int Parent, string Name)>();
        var entry = new NativeMethods.ProcessEntry { Size = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry>(), ExeFile = "" };
        if (!NativeMethods.Process32First(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Снимок процессов пуст или недоступен");
        do
        {
            if (entry.ProcessId <= int.MaxValue) table[(int)entry.ProcessId] = (checked((int)entry.ParentProcessId), entry.ExeFile);
            entry.Size = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry>();
        } while (NativeMethods.Process32Next(snapshot, ref entry));
        if (Marshal.GetLastWin32Error() != 18) throw new Win32Exception(Marshal.GetLastWin32Error(), "Снимок процессов получен не полностью");
        return table;
    }

    private static Dictionary<int, List<nint>> ReadWindows()
    {
        var windows = new Dictionary<int, List<nint>>();
        if (!NativeMethods.EnumWindows((window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window)) return true;
            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (!windows.TryGetValue(processId, out var list)) windows[processId] = list = [];
            list.Add(window);
            return true;
        }, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Перечень окон недоступен");
        return windows;
    }

    private static WindowInfo ReadWindowInfo(nint window)
    {
        var title = new StringBuilder(1024);
        var className = new StringBuilder(256);
        NativeMethods.GetWindowText(window, title, title.Capacity);
        NativeMethods.GetClassName(window, className, className.Capacity);
        return new(title.ToString(), className.ToString(), NativeMethods.IsIconic(window));
    }

    private static long ReadStart(SafeProcessHandle handle)
    {
        if (!NativeMethods.GetProcessTimes(handle, out var creation, out _, out _, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return DateTime.FromFileTimeUtc(creation).Ticks;
    }
    private static string ReadPath(SafeProcessHandle handle)
    {
        var path = new StringBuilder(32768);
        var size = path.Capacity;
        if (!NativeMethods.QueryFullProcessImageName(handle, 0, path, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Путь .exe недоступен");
        return ProcessSafety.ResolveExecutablePath(path.ToString());
    }
    private static bool Matches(SafeProcessHandle handle, ProcessInfo process)
    {
        try
        {
            return process.StartTimeUtcTicks > 0 && process.FullPath is not null &&
                NativeMethods.WaitForSingleObject(handle, 0) == NativeMethods.WaitTimeout &&
                ReadStart(handle) == process.StartTimeUtcTicks && string.Equals(ReadPath(handle), process.FullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
    private static ProcessInfo? FindSame(ProcessScan scan, ProcessInfo process) => scan.Processes.FirstOrDefault(item =>
        item.Id == process.Id && item.StartTimeUtcTicks == process.StartTimeUtcTicks && process.StartTimeUtcTicks > 0 &&
        string.Equals(item.FullPath, process.FullPath, StringComparison.OrdinalIgnoreCase));
    private static bool CanAct(ProcessScan scan, ProcessInfo? current, SafeProcessHandle handle, Func<ProcessScan, ProcessInfo, bool> authorize) =>
        scan.SafetyInventoryAvailable && current?.SafetyVerified == true && Matches(handle, current) && authorize(scan, current);

    private static SafeProcessHandle OpenCloseHandle(int id, bool allowForce, out bool canTerminate)
    {
        canTerminate = false;
        if (allowForce)
        {
            var full = NativeMethods.OpenProcess(NativeMethods.QueryLimited | NativeMethods.Synchronize | NativeMethods.Terminate, false, id);
            if (!full.IsInvalid) { canTerminate = true; return full; }
            full.Dispose();
        }
        return NativeMethods.OpenProcess(NativeMethods.QueryLimited | NativeMethods.Synchronize, false, id);
    }

    private static (string Image, string CommandLine, string WorkingDirectory) ReadLaunchParameters(SafeProcessHandle process)
    {
        var basic = new byte[48];
        if (NativeMethods.NtQueryInformationProcess(process, 0, basic, (uint)basic.Length, out _) != 0) throw new InvalidOperationException("PEB процесса недоступен.");
        var peb = checked((nint)BitConverter.ToInt64(basic, 8));
        var header = ReadMemory(process, peb, 40);
        var parameters = checked((nint)BitConverter.ToInt64(header, 32));
        var fields = ReadMemory(process, parameters, 128);
        return (ReadUnicode(process, fields, 96), ReadUnicode(process, fields, 112), ReadUnicode(process, fields, 56));
    }
    private static byte[] ReadMemory(SafeProcessHandle process, nint address, int length)
    {
        if (address < 65536 || length <= 0 || length > 65536) throw new InvalidOperationException("Недопустимый адрес или размер параметров процесса.");
        var bytes = new byte[length];
        if (!NativeMethods.ReadProcessMemory(process, address, bytes, (nuint)length, out var read) || read != (nuint)length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Параметры процесса прочитаны не полностью");
        return bytes;
    }
    private static string ReadUnicode(SafeProcessHandle process, byte[] fields, int offset)
    {
        var length = BitConverter.ToUInt16(fields, offset);
        var maximum = BitConverter.ToUInt16(fields, offset + 2);
        var address = checked((nint)BitConverter.ToInt64(fields, offset + 8));
        if (length == 0 || length > maximum || (length & 1) != 0 || length > 65534) throw new InvalidOperationException("Параметр запуска пуст или имеет недопустимую длину.");
        var value = Encoding.Unicode.GetString(ReadMemory(process, address, length));
        if (value.Contains('\0')) throw new InvalidOperationException("Параметры запуска содержат скрытый разделитель.");
        return value;
    }

    private static bool TryExtractArguments(string command, string actualPath, out string arguments, out string? reason)
    {
        arguments = ""; reason = null;
        if (string.IsNullOrWhiteSpace(command) || command.Contains('\0') || command.Length >= 32767)
        { reason = "Командная строка недоступна или недопустима."; return false; }
        var cursor = 0;
        while (cursor < command.Length && char.IsWhiteSpace(command[cursor])) cursor++;
        string executable;
        if (command[cursor] == '"')
        {
            var end = command.IndexOf('"', cursor + 1);
            if (end < 0) { reason = "Командная строка содержит незакрытую кавычку."; return false; }
            executable = command[(cursor + 1)..end]; cursor = end + 1;
            if (cursor < command.Length && !char.IsWhiteSpace(command[cursor])) { reason = "Неоднозначный первый аргумент запуска."; return false; }
        }
        else
        {
            var end = cursor;
            while (end < command.Length && !char.IsWhiteSpace(command[end])) end++;
            executable = command[cursor..end]; cursor = end;
        }
        if (!Path.IsPathFullyQualified(executable) || !string.Equals(ProcessSafety.ResolveExecutablePath(executable), actualPath, StringComparison.OrdinalIgnoreCase))
        { reason = "Исходная командная строка не содержит однозначный полный путь к тому же .exe."; return false; }
        arguments = command[cursor..].TrimStart();
        reason = UnsafeArguments(arguments);
        return reason is null;
    }

    private static string? UnsafeArguments(string arguments)
    {
        if (arguments.Length >= 32767 || arguments.Any(c => c is '\0' or '\r' or '\n')) return "Недопустимые символы в аргументах запуска.";
        if (arguments.Length == 0) return null;
        var pointer = NativeMethods.CommandLineToArgvW("app.exe " + arguments, out var count);
        if (pointer == 0) return "Не удалось разобрать аргументы запуска.";
        try
        {
            var dangerous = new[] { "password", "passwd", "token", "secret", "apikey", "api-key", "credential", "session", "auth", "ticket",
                "--type", "--no-sandbox", "--utility", "--renderer", "--gpu-process", "--extension-process", "--parent", "--child", "--pipe",
                "--handle", "--pid", "/automation", "/embedding", "/dde", "--restart", "--update", "--install", "--uninstall", "--background", "--service", "--headless" };
            var scripts = new[] { ".ps1", ".bat", ".cmd", ".vbs", ".js", ".wsf", ".hta", ".msi", ".msp", ".reg", ".lnk" };
            for (var i = 1; i < count; i++)
            {
                var argument = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size)) ?? "";
                if (dangerous.Any(x => argument.Contains(x, StringComparison.OrdinalIgnoreCase)) || scripts.Any(x => argument.EndsWith(x, StringComparison.OrdinalIgnoreCase)) ||
                    argument.Contains("://", StringComparison.Ordinal) || argument.Contains("=http", StringComparison.OrdinalIgnoreCase))
                    return "Аргументы содержат чувствительные данные, URL, сценарий или служебные параметры; безопасный повторный запуск запрещён.";
                if (argument.StartsWith("\\\\", StringComparison.Ordinal)) return "Аргументы с сетевыми путями не будут повторно использованы.";
                if (Path.IsPathFullyQualified(argument) && !argument.StartsWith('-') && !File.Exists(argument) && !Directory.Exists(argument))
                    return "Файл или папка из аргументов больше недоступны; возврат запрещён.";
            }
        }
        finally { NativeMethods.LocalFree(pointer); }
        return null;
    }

    private static string HashFile(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    private static string? CheckDuplicates(string path)
    {
        var fileName = Path.GetFileName(path);
        foreach (var pair in ReadProcessTable())
        {
            // System inaccessible images are protected by platform rules and cannot duplicate a captured eligible GUI executable.
            if (pair.Key <= 4) continue;
            using var handle = NativeMethods.OpenProcess(NativeMethods.QueryLimited | NativeMethods.Synchronize, false, pair.Key);
            if (handle.IsInvalid)
            {
                if (string.Equals(pair.Value.Name, fileName, StringComparison.OrdinalIgnoreCase))
                    return "Одноимённый процесс недоступен для проверки; запуск дубликата запрещён.";
                continue;
            }
            if (NativeMethods.WaitForSingleObject(handle, 0) == NativeMethods.WaitObject0) continue;
            try
            {
                if (string.Equals(ReadPath(handle), path, StringComparison.OrdinalIgnoreCase)) return "Приложение уже работает; дубликат не запущен.";
            }
            catch
            {
                if (string.Equals(pair.Value.Name, fileName, StringComparison.OrdinalIgnoreCase))
                    return "Путь одноимённого процесса не удалось проверить; запуск запрещён.";
            }
        }
        return null;
    }

}
