namespace GameMode.Core;

public enum ModeState { Active, Waiting, ForcedOff }
public enum RestoreState { Pending, Started, Skipped, Failed, Interrupted }

public sealed class ExecutableRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public string? FullPath { get; set; }
    public string? ExecutableName { get; set; }
    public bool Enabled { get; set; } = true;
    public bool BuiltIn { get; set; }
}

public sealed class AppSettings
{
    public int ThresholdMiB { get; set; } = 200;
    public int PollSeconds { get; set; } = 10;
    public bool Enabled { get; set; }
    public bool RiskAcknowledged { get; set; }
    public bool RestoreApps { get; set; } = true;
    public bool AllowForceClose { get; set; } = true;
    public bool LaunchAtLogin { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool ExperimentalGameDetection { get; set; }
    public List<ExecutableRule> Exclusions { get; set; } = [];
    public List<ExecutableRule> Games { get; set; } =
    [
        new() { Label = "Dota 2", ExecutableName = "dota2.exe", BuiltIn = true },
        new() { Label = "Counter-Strike 2", ExecutableName = "cs2.exe", BuiltIn = true },
        new() { Label = "Roblox", ExecutableName = "RobloxPlayerBeta.exe", BuiltIn = true }
    ];
    public List<string> NoRestorePaths { get; set; } = [];
}

public sealed record ProcessInfo(int Id, long StartTimeUtcTicks, int ParentId, string Name,
    string? FullPath, long WorkingSetBytes, bool SafetyVerified, string? ProtectedReason,
    bool HasVisibleWindow, int SessionId, string? GameLibraryHint = null)
{
    public string Identity => $"{Id}:{StartTimeUtcTicks}";
    public double WorkingSetMiB => WorkingSetBytes / 1048576d;
}

public sealed record ProcessScan(IReadOnlyList<ProcessInfo> Processes, bool SafetyInventoryAvailable, string? Error = null);
public sealed record WindowInfo(string Title, string ClassName, bool WasMinimized);
public sealed record LaunchInfo(string FullPath, string Arguments, string WorkingDirectory,
    IReadOnlyList<WindowInfo> Windows, string ExecutableSha256);
public sealed record RestartCapture(LaunchInfo? Launch, string? Reason);
public sealed record CloseResult(bool Closed, bool Forced, string Detail);
public sealed record LaunchResult(bool Started, string Detail);

public sealed class RestartEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset ClosedAt { get; set; } = DateTimeOffset.Now;
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public LaunchInfo? Launch { get; set; }
    public bool RestoreEnabled { get; set; } = true;
    public RestoreState State { get; set; } = RestoreState.Pending;
    public string Detail { get; set; } = "";
}

public sealed record LogEntry(DateTimeOffset Time, string Level, string Message);
public sealed record RuntimeView(ModeState State, string Explanation, IReadOnlyList<ProcessInfo> Games,
    IReadOnlyList<ProcessInfo> Processes, IReadOnlyList<RestartEntry> RestoreQueue,
    IReadOnlyList<LogEntry> Log, long ClosedWorkingSetBytes, int ClosedCount,
    IReadOnlyDictionary<Guid, DateTimeOffset> LastRuleMatches);

public interface IProcessPlatform
{
    Task<ProcessScan> ScanAsync(CancellationToken cancellationToken);
    Task<RestartCapture> CaptureRestartAsync(ProcessInfo process, CancellationToken cancellationToken);
    // Revalidate identity, platform protections and this callback immediately before WM_CLOSE and force termination.
    Task<CloseResult> CloseAsync(ProcessInfo process, bool allowForce,
        Func<ProcessScan, ProcessInfo, bool> authorize, CancellationToken cancellationToken);
    // Must verify identity/hash, reject unsafe executables and duplicates again before creation.
    Task<LaunchResult> RestartAsync(LaunchInfo launch, Func<ProcessScan, bool> authorize, CancellationToken cancellationToken);
}

public interface IGameModeLog
{
    IReadOnlyList<LogEntry> Entries { get; }
    void Write(string level, string message);
    event Action<LogEntry>? Added;
}
