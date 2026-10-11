using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using GameMode.Core;
using Microsoft.Win32.SafeHandles;

namespace GameMode.Windows.Processes;

internal sealed class ProcessSafety
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "idle", "system", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "dwm", "explorer", "sihost", "fontdrvhost", "taskhostw", "conhost", "ctfmon",
        "shellexperiencehost", "startmenuexperiencehost", "searchhost", "searchapp", "searchindexer",
        "runtimebroker", "applicationframehost", "dllhost", "audiodg", "securityhealthservice",
        "securityhealthsystray", "msmpeng", "nissrv", "mssense", "smartscreen", "sgrmbroker", "spoolsv",
        "wudfhost", "wudfrd", "sppsvc", "trustedinstaller", "tiworker", "dashost", "wmiprvse", "mousocoreworker",
        "msiexec", "setup", "update", "updater", "uninstall", "uninstaller", "regsvr32", "rundll32",
        "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "wsl", "wslhost", "windowsterminal",
        "vmmem", "vmmemwsl", "vmcompute", "vmwp", "taskmgr", "consent", "credentialuibroker"
    };
    private static readonly string[] InfrastructureWords =
    [
        "antivirus", "antimalware", "defender", "securityhealth", "kaspersky", "avast", "avg technologies",
        "bitdefender", "eset", "malwarebytes", "mcafee", "norton", "symantec", "sophos", "trend micro",
        "crowdstrike", "sentinelone", "fortinet", "f-secure", "360safe", "360total", "drweb", "dr.web",
        "nvidia", "nvcontainer", "nvdisplay", "amd", "radeon", "intel", "realtek", "nahimic", "a-volute",
        "corsair", "icue", "logitech", "lghub", "razer", "steelseries", "asus", "armoury", "msi center",
        "gigabyte", "aorus", "lenovo", "dell", "hewlett", "hp inc", "acer", "driver", "anticheat",
        "easyanticheat", "battleye", "vgc", "vgtray", "faceit", "ricochet"
    ];
    private static readonly string[] UnsafeRestartWords =
    ["setup", "install", "uninstall", "update", "bootstrap", "crash", "reporter", "helper", "host", "service", "broker", "launcher", "maintenance"];
    private readonly ConcurrentDictionary<string, (long Length, long Ticks, bool Microsoft)> signatureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string windowsDirectory = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd('\\');

    internal sealed record Inventory(HashSet<int> Services, HashSet<int> OwnFamily, HashSet<string> AntivirusDirectories,
        string CurrentSid, int CurrentSession, bool Available, string? Error);

    internal Inventory ReadInventory(IReadOnlyDictionary<int, (int Parent, string Name)> processes)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new InvalidOperationException("Не удалось определить SID пользователя.");
            using var ownHandle = NativeMethods.OpenProcess(NativeMethods.QueryLimited, false, Environment.ProcessId);
            if (ownHandle.IsInvalid || TokenProtection(ownHandle, sid) is not null)
                throw new InvalidOperationException("Запустите Game Mode как обычный пользователь без повышения прав. Возврат приложений с изменёнными привилегиями запрещён.");
            if (!NativeMethods.ProcessIdToSessionId(Environment.ProcessId, out var session))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось определить сеанс Game Mode.");
            var services = ReadServices();
            var antivirusDirectories = ReadAntivirusDirectories();
            var family = new HashSet<int> { Environment.ProcessId };
            var ancestor = Environment.ProcessId;
            while (processes.TryGetValue(ancestor, out var row) && row.Parent > 0 && family.Add(row.Parent)) ancestor = row.Parent;
            // Protect descendants of Game Mode and of every service; ancestry alone does not protect siblings of Game Mode.
            var descendants = new HashSet<int>(services) { Environment.ProcessId };
            bool changed;
            do
            {
                changed = false;
                foreach (var pair in processes)
                    if (descendants.Contains(pair.Value.Parent) && descendants.Add(pair.Key)) changed = true;
            } while (changed);
            family.UnionWith(descendants);
            return new(services, family, antivirusDirectories, sid, checked((int)session), true, null);
        }
        catch (Exception error)
        {
            return new([], [], new(StringComparer.OrdinalIgnoreCase), "", -1, false,
                $"Защитный перечень служб/антивирусов недоступен: {error.Message}. Закрытие процессов запрещено.");
        }
    }

    internal string? ProtectionReason(SafeProcessHandle handle, int id, int session, string path, bool visible, Inventory inventory)
    {
        if (!inventory.Available) return inventory.Error;
        if (id <= 4 || inventory.OwnFamily.Contains(id)) return "Game Mode, его предок/потомок или компонент службы";
        if (session == 0 || session != inventory.CurrentSession) return "Системный или другой сеанс Windows";
        if (inventory.Services.Contains(id)) return "Процесс службы Windows";
        if (!NativeMethods.IsProcessCritical(handle, out var critical)) return "Не удалось проверить критичность процесса";
        if (critical) return "Критически важный процесс Windows";
        var protection = new byte[1];
        if (NativeMethods.NtQueryInformationProcess(handle, 61, protection, 1, out _) != 0)
            return "Не удалось проверить защищённость процесса";
        if (protection[0] != 0) return "Защищённый процесс Windows (PPL/PP)";
        var tokenReason = TokenProtection(handle, inventory.CurrentSid);
        if (tokenReason is not null) return tokenReason;
        var executableReason = ExecutableProtection(path, inventory.AntivirusDirectories);
        if (executableReason is not null) return executableReason;
        if (!visible) return "Нет видимого окна: фоновый компонент защищён";
        return null;
    }

    internal string? ExecutableProtection(string path, HashSet<string>? antivirusDirectories = null)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            return "Исполняемый файл не является локальным .exe";
        if (NativeMethods.GetDriveType(Path.GetPathRoot(path)!) != 3) return "Файл расположен не на локальном фиксированном диске";
        if (IsUnder(path, windowsDirectory) || path.Split('\\').Any(x => x.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase)))
            return "Компонент Windows или пакетное приложение WindowsApps";
        var name = Path.GetFileNameWithoutExtension(path);
        if (ProtectedNames.Contains(name)) return "Оболочка, инфраструктура Windows или небезопасный вспомогательный процесс";
        if (InfrastructureWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase) ||
            path.Contains(word, StringComparison.OrdinalIgnoreCase))) return "Антивирус, драйвер или критически важный компонент поставщика";
        if (antivirusDirectories?.Any(directory => IsUnder(path, directory)) == true) return "Приложение установленного антивируса";
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return "Исполняемый файл недоступен";
            if (!signatureCache.TryGetValue(path, out var cached) || cached.Length != file.Length || cached.Ticks != file.LastWriteTimeUtc.Ticks)
            {
                cached = (file.Length, file.LastWriteTimeUtc.Ticks, IsMicrosoftSigned(path));
                signatureCache[path] = cached;
            }
            if (cached.Microsoft) return "Подписанный Microsoft компонент защищён консервативным правилом";
        }
        catch (Exception error) { return $"Не удалось проверить исполняемый файл: {error.Message}"; }
        return null;
    }

    internal string? RestartExecutableReason(string path, HashSet<string>? antivirusDirectories = null)
    {
        var reason = ExecutableProtection(path, antivirusDirectories);
        if (reason is not null) return reason;
        var name = Path.GetFileNameWithoutExtension(path);
        if (UnsafeRestartWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return "Повторный запуск установщика, обновления, лаунчера или вспомогательного процесса запрещён";
        var temp = Path.GetFullPath(Path.GetTempPath());
        if (IsUnder(path, temp)) return "Повторный запуск файла из временной папки запрещён";
        return null;
    }

    internal static bool IsUnder(string path, string directory) => path.Equals(directory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    internal static string ResolveExecutablePath(string path)
    {
        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new StringBuilder(32768);
        var size = NativeMethods.GetFinalPathNameByHandle(file, buffer, (uint)buffer.Capacity, 0);
        if (size == 0 || size >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось проверить фактический путь .exe");
        var finalPath = buffer.ToString();
        if (finalPath.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Сетевой исполняемый файл защищён.");
        if (finalPath.StartsWith("\\\\?\\", StringComparison.Ordinal)) finalPath = finalPath[4..];
        return Path.GetFullPath(finalPath);
    }

    private static string? TokenProtection(SafeProcessHandle handle, string currentSid)
    {
        if (!NativeMethods.OpenProcessToken(handle, NativeMethods.TokenQuery, out var token)) return "Недоступен токен процесса (возможно, нужны повышенные права)";
        using (token)
        {
            var allocated = new List<nint>();
            nint ReadOwned(int type) { var pointer = ReadToken(token, type); allocated.Add(pointer); return pointer; }
            try
            {
                var user = ReadOwned(1);
                if (new SecurityIdentifier(Marshal.ReadIntPtr(user)).Value != currentSid) return "Процесс другого пользователя или системной учётной записи";
                var elevation = ReadOwned(20);
                var elevated = Marshal.ReadInt32(elevation) != 0;
                if (elevated) return "Процесс запущен с повышенными правами";
                var integrity = ReadOwned(25);
                var integritySid = new SecurityIdentifier(Marshal.ReadIntPtr(integrity)).Value;
                if (!uint.TryParse(integritySid.Split('-')[^1], out var level) || level >= 0x3000) return "Высокий или неизвестный уровень целостности";
                var uiAccess = ReadOwned(26);
                var uiAllowed = Marshal.ReadInt32(uiAccess) != 0;
                if (uiAllowed) return "Компонент специальных возможностей UIAccess";
                var container = ReadOwned(29);
                var appContainer = Marshal.ReadInt32(container) != 0;
                if (appContainer) return "Изолированное приложение AppContainer";
                return null;
            }
            catch { return "Не удалось проверить владельца и уровень доступа процесса"; }
            finally { foreach (var pointer in allocated) Marshal.FreeHGlobal(pointer); }
        }
    }

    private static nint ReadToken(SafeTokenHandle token, int type)
    {
        NativeMethods.GetTokenInformation(token, type, 0, 0, out var size);
        if (size == 0 || size > 65536) throw new Win32Exception(Marshal.GetLastWin32Error());
        var data = Marshal.AllocHGlobal(checked((int)size));
        if (NativeMethods.GetTokenInformation(token, type, data, size, out _)) return data;
        Marshal.FreeHGlobal(data);
        throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static HashSet<int> ReadServices()
    {
        using var manager = NativeMethods.OpenSCManager(null, null, 4);
        if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Перечень служб недоступен");
        var result = new HashSet<int>();
        uint resume = 0;
        const uint size = 256 * 1024;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            while (true)
            {
                var previous = resume;
                var succeeded = NativeMethods.EnumServicesStatusEx(manager, 0, 0x3B, 3, buffer, size, out _, out var count, ref resume, null);
                var error = succeeded ? 0 : Marshal.GetLastWin32Error();
                if (!succeeded && error != NativeMethods.ErrorMoreData) throw new Win32Exception(error, "Ошибка получения перечня служб");
                var entrySize = Marshal.SizeOf<NativeMethods.ServiceEntry>();
                if ((ulong)count * (uint)entrySize > size) throw new InvalidOperationException("Некорректный размер перечня служб.");
                for (var i = 0; i < count; i++)
                {
                    var entry = Marshal.PtrToStructure<NativeMethods.ServiceEntry>(buffer + i * entrySize);
                    if (entry.Status.ProcessId > 0 && entry.Status.ProcessId <= int.MaxValue) result.Add((int)entry.Status.ProcessId);
                }
                if (succeeded) break;
                if (resume == previous || count == 0) throw new InvalidOperationException("Перечень служб получен не полностью.");
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }

    private static HashSet<string> ReadAntivirusDirectories()
    {
        object? locator = null, service = null, products = null;
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator") ?? throw new InvalidOperationException("WMI не установлен.");
            locator = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Не удалось открыть WMI.");
            service = ((dynamic)locator).ConnectServer(".", "root\\SecurityCenter2");
            products = ((dynamic)service).ExecQuery("SELECT * FROM AntiVirusProduct", "WQL", 0);
            int count = ((dynamic)products).Count;
            if (count < 0 || count > 100) throw new InvalidOperationException("Некорректный перечень антивирусов.");
            for (var index = 0; index < count; index++)
            {
                object product = ((dynamic)products).ItemIndex(index);
                object? properties = null;
                try
                {
                    properties = ((dynamic)product).Properties_;
                    string ReadProperty(string name)
                    {
                        object property = ((dynamic)properties).Item(name, 0);
                        try { return Convert.ToString(((dynamic)property).Value) ?? ""; }
                        finally { if (Marshal.IsComObject(property)) Marshal.FinalReleaseComObject(property); }
                    }
                    string productPath = ReadProperty("pathToSignedProductExe");
                    string reportingPath = ReadProperty("pathToSignedReportingExe");
                    var covered = false;
                    foreach (var raw in new[] { productPath, reportingPath })
                    {
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        var expanded = Environment.ExpandEnvironmentVariables(raw.Trim());
                        if (expanded.StartsWith('"'))
                        {
                            var quote = expanded.IndexOf('"', 1);
                            if (quote > 1) expanded = expanded[1..quote];
                        }
                        else
                        {
                            var extension = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                            if (extension >= 0) expanded = expanded[..(extension + 4)];
                        }
                        // SecurityCenter also exposes URI-valued reporting entries; those are not executable paths.
                        if (!Path.IsPathFullyQualified(expanded) || !expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        var directory = Path.GetDirectoryName(Path.GetFullPath(expanded));
                        if (directory is not null && directory.Length > 3) { directories.Add(directory); covered = true; }
                    }
                    if (!covered) throw new InvalidOperationException("Пути зарегистрированного антивируса не удалось надёжно определить.");
                }
                finally
                {
                    if (properties is not null && Marshal.IsComObject(properties)) Marshal.FinalReleaseComObject(properties);
                    if (Marshal.IsComObject(product)) Marshal.FinalReleaseComObject(product);
                }
            }
        }
        finally
        {
            foreach (var item in new[] { products, service, locator })
                if (item is not null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item);
        }
        return directories;
    }

    private static bool IsMicrosoftSigned(string path)
    {
        X509Certificate2 certificate;
        try
        {
#pragma warning disable SYSLIB0057
            certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
        }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
        using (certificate)
        {
            if (!certificate.Subject.Contains("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase)) return false;
            var file = new NativeMethods.WinTrustFileInfo { Size = (uint)Marshal.SizeOf<NativeMethods.WinTrustFileInfo>(), Path = path };
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WinTrustFileInfo>());
            Marshal.StructureToPtr(file, pointer, false);
            try
            {
                var data = new NativeMethods.WinTrustData
                {
                    Size = (uint)Marshal.SizeOf<NativeMethods.WinTrustData>(), UiChoice = 2, UnionChoice = 1,
                    FileInfo = pointer, ProviderFlags = 0x1000 // Use cached revocation information; no networking or prompts.
                };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                // A Microsoft-labelled signature is protected even if its chain cannot currently be verified.
                _ = NativeMethods.WinVerifyTrust(0, ref action, ref data);
                return true;
            }
            finally { Marshal.DestroyStructure<NativeMethods.WinTrustFileInfo>(pointer); Marshal.FreeHGlobal(pointer); }
        }
    }
}
