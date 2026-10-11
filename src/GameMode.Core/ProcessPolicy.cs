namespace GameMode.Core;

public static class ProcessPolicy
{
    private static readonly string[] BuiltInGameExecutables = ["dota2.exe", "cs2.exe", "RobloxPlayerBeta.exe"];
    public static bool SamePath(string? a, string? b) => a is not null && b is not null && string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    public static bool Matches(ExecutableRule rule, ProcessInfo process)
    {
        if (process.FullPath is null) return false;
        return rule.FullPath is not null ? SamePath(rule.FullPath, process.FullPath) : string.Equals(rule.ExecutableName, Path.GetFileName(process.FullPath), StringComparison.OrdinalIgnoreCase);
    }
    public static IReadOnlyList<ProcessInfo> DetectGames(AppSettings settings, IReadOnlyList<ProcessInfo> processes) =>
        processes.Where(p => settings.Games.Any(r => r.Enabled && Matches(r, p))).ToArray();

    // Disabled detection rules still protect their executable. Intrinsic games stay protected even after rule removal.
    public static HashSet<int> ProtectedGameFamily(AppSettings settings, IReadOnlyList<ProcessInfo> processes)
    {
        var roots = processes.Where(p => settings.Games.Any(r => Matches(r, p)) || BuiltInGameExecutables.Any(n => string.Equals(n, Path.GetFileName(p.FullPath), StringComparison.OrdinalIgnoreCase))).Select(p => p.Id).ToHashSet();
        var family = new HashSet<int>(roots);
        bool changed;
        do { changed = false; foreach (var p in processes) if (family.Contains(p.ParentId)) changed |= family.Add(p.Id); } while (changed);
        foreach (var root in roots)
        {
            var current = root; var visited = new HashSet<int>();
            while (visited.Add(current))
            { var p = processes.FirstOrDefault(p => p.Id == current); if (p is null || p.ParentId <= 0) break; family.Add(p.ParentId); current = p.ParentId; }
        }
        return family;
    }

    public static bool CanClose(ProcessInfo process, ProcessScan scan, AppSettings settings, int selfPid, out string reason)
    {
        reason = "";
        if (!settings.Enabled || !settings.RiskAcknowledged) reason = "Режим выключен.";
        else if (!scan.SafetyInventoryAvailable) reason = "Не удалось проверить защиту служб и антивирусов.";
        else if (DetectGames(settings, scan.Processes).Count == 0) reason = "Игра завершилась или автодетект отключён.";
        else if (process.Id <= 4 || process.Id == selfPid) reason = "Системный процесс или Game Mode.";
        else if (!process.SafetyVerified || process.ProtectedReason is not null) reason = process.ProtectedReason ?? "Безопасность процесса не установлена.";
        else if (process.FullPath is null || !process.HasVisibleWindow) reason = "Нет доступного пути или обычного окна приложения.";
        else if (process.WorkingSetBytes <= (long)settings.ThresholdMiB * 1048576) reason = "Working Set не превышает порог.";
        else if (settings.Exclusions.Any(r => Matches(r, process))) reason = "Процесс в исключениях.";
        else if (ProtectedGameFamily(settings, scan.Processes).Contains(process.Id)) reason = "Игра или связанный с ней процесс.";
        return reason.Length == 0;
    }
    public static bool CanRestore(RestartEntry entry, ProcessScan scan, AppSettings settings, out string reason)
    {
        reason = "";
        var p = new ProcessInfo(0, 0, 0, entry.Name, entry.Path, 0, true, null, true, 1);
        if (entry.State != RestoreState.Pending) reason = "Закрытие не подтверждено или возврат уже обработан.";
        else if (!settings.RestoreApps || !entry.RestoreEnabled || settings.NoRestorePaths.Any(path => SamePath(path, entry.Path))) reason = "Возврат отключён пользователем.";
        else if (!scan.SafetyInventoryAvailable) reason = "Безопасность повторного запуска сейчас не проверена.";
        else if (entry.Launch is null) reason = "Недостаточно данных для безопасного повторного запуска.";
        else if (settings.Exclusions.Any(r => Matches(r, p))) reason = "Приложение добавлено в исключения.";
        else if (settings.Games.Any(r => Matches(r, p)) || BuiltInGameExecutables.Any(n => string.Equals(n, Path.GetFileName(entry.Path), StringComparison.OrdinalIgnoreCase))) reason = "Игра или программа автодетекта не перезапускается.";
        else if (scan.Processes.Any(existing => SamePath(existing.FullPath, entry.Path))) reason = "Приложение уже работает; дубликат не запущен.";
        return reason.Length == 0;
    }
}
