using GameMode.Core;

const long MiB = 1048576;
const string AppPath = @"C:\TestApps\editor.exe";
var tests = new List<(string Name, Func<Task> Run)>
{
    ("defaults and executable matching", DefaultsAndMatching),
    ("strict Working Set threshold", StrictThreshold),
    ("games and family protected", ProtectedGamesAndFamily),
    ("critical, unknown, service and self protected", ProtectedProcesses),
    ("exclusions use exact full path", Exclusions),
    ("incomplete inventory blocks cleanup and restore", IncompleteInventory),
    ("three states, disabled rules, multiple games", StateTransitions),
    ("confirmed closures restored once with captured launch data", RestoreOnce),
    ("existing instance prevents duplicate", PreventDuplicate),
    ("new exclusion prevents restore", NewExclusion),
    ("per-app and global restore switches", RestoreSwitches),
    ("missing capture and failed closure not restored", FailedCloseAndCapture),
    ("authorization rechecked before close", RevalidateClose),
    ("restore policy rechecked immediately before creation", RevalidateRestore),
    ("settings persisted and malformed data fails closed", Persistence),
    ("durable journal required before close", JournalFailure),
    ("interrupted previous session never auto-restored", InterruptedSession),
    ("refresh and preview cannot close apps", ReadOnlyModes),
    ("largest eligible Working Set closes first", HighestMemoryFirst),
    ("session summaries and notification cooldown", QuietNotifications),
    ("notification opt-out never replays old events", DisabledNotifications),
    ("experimental Steam detection remains opt-in and protected", ExperimentalGames),
    ("QoL settings and Beta version persist", QolSettingsPersist),
    ("Steam manifests respect directory boundaries", SteamManifestBoundaries),
    ("batch restore opt-out persists for every selected app", BatchRestoreOptOut)
};
var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed. All processes in these checks are fake.");
return failures == 0 ? 0 : 1;

Task DefaultsAndMatching()
{
    var settings = new AppSettings();
    Equal(200, settings.ThresholdMiB); Equal(10, settings.PollSeconds); False(settings.Enabled);
    Equal(3, settings.Games.Count);
    var game = Proc(100101, @"C:\Games\dota2.exe", 300);
    True(ProcessPolicy.Matches(settings.Games[0], game));
    False(ProcessPolicy.Matches(settings.Games[0], game with { Name = "dota2", FullPath = @"C:\Games\other.exe" }));
    False(ProcessPolicy.Matches(settings.Games[0], game with { FullPath = null }));
    True(ProcessPolicy.Matches(Rule(AppPath), Proc(100202, @"c:\testapps\EDITOR.EXE", 300)));
    False(ProcessPolicy.Matches(Rule(AppPath), Proc(100202, @"C:\Other\editor.exe", 300)));
    return Task.CompletedTask;
}

Task StrictThreshold()
{
    var settings = EnabledSettings();
    var game = Game();
    var app = Proc(100202, AppPath, 200);
    False(CanClose(app, [game, app], settings));
    app = app with { WorkingSetBytes = 200 * MiB + 1 };
    True(CanClose(app, [game, app], settings));
    settings.ThresholdMiB = 300;
    False(CanClose(app, [game, app], settings));
    return Task.CompletedTask;
}

Task ProtectedGamesAndFamily()
{
    var settings = EnabledSettings();
    var launcher = Proc(100100, @"C:\Games\launcher.exe", 500);
    var game = Game() with { ParentId = launcher.Id };
    var child = Proc(100102, @"C:\Games\renderer.exe", 400) with { ParentId = game.Id };
    var grandchild = Proc(100103, @"C:\Games\helper.exe", 400) with { ParentId = child.Id };
    var disabledGame = Proc(100105, @"C:\Games\cs2.exe", 400);
    settings.Games.Single(r => r.ExecutableName == "cs2.exe").Enabled = false;
    ProcessInfo[] all = [launcher, game, child, grandchild, disabledGame];
    foreach (var protectedProcess in all) False(CanClose(protectedProcess, all, settings));
    settings.Games.RemoveAll(r => r.ExecutableName == "cs2.exe");
    False(CanClose(disabledGame, all, settings));
    var customGame = Proc(100106, @"C:\Games\custom.exe", 400);
    settings.Games.Add(Rule(customGame.FullPath!));
    False(CanClose(customGame, [game, customGame], settings));
    return Task.CompletedTask;
}

Task ProtectedProcesses()
{
    var settings = EnabledSettings(); var app = Proc(100202, AppPath, 300); var game = Game();
    False(CanClose(app with { Id = Environment.ProcessId }, [game, app], settings));
    False(CanClose(app with { Id = 4 }, [game, app], settings));
    foreach (var reason in new[] { "Оболочка Windows", "Служба", "Антивирус", "Драйвер", "Критический процесс" })
        False(CanClose(app with { ProtectedReason = reason }, [game, app], settings));
    False(CanClose(app with { SafetyVerified = false }, [game, app], settings));
    False(CanClose(app with { FullPath = null }, [game, app], settings));
    False(CanClose(app with { HasVisibleWindow = false }, [game, app], settings));
    settings.RiskAcknowledged = false;
    False(CanClose(app, [game, app], settings));
    return Task.CompletedTask;
}

Task Exclusions()
{
    var settings = EnabledSettings(); var app = Proc(100202, AppPath, 300); var game = Game();
    settings.Exclusions.Add(Rule(AppPath));
    False(CanClose(app, [game, app], settings));
    var other = app with { FullPath = @"C:\Other\editor.exe" };
    True(CanClose(other, [game, other], settings));
    settings.Exclusions.Add(new ExecutableRule { ExecutableName = "editor.exe", Label = "all editor processes" });
    False(CanClose(other, [game, other], settings));
    return Task.CompletedTask;
}

async Task IncompleteInventory()
{
    await using var f = new Fixture();
    f.Platform.Processes.AddRange([Game(), Proc(100202, AppPath, 300)]);
    f.Platform.InventoryAvailable = false;
    await f.Runtime.TickAsync();
    Equal(0, f.Platform.CloseAttempts.Count); Equal(0, f.Runtime.View.RestoreQueue.Count);
    Contains("приостановлена", f.Runtime.View.Explanation);
    f.Platform.InventoryAvailable = true;
    await f.Runtime.TickAsync(); Equal(1, f.Platform.Closed.Count);
    f.Platform.Processes.Clear(); f.Platform.InventoryAvailable = false;
    await f.Runtime.TickAsync(); Equal(0, f.Platform.Restarted.Count);
    f.Platform.InventoryAvailable = true;
    await f.Runtime.TickAsync(); Equal(1, f.Platform.Restarted.Count);
}

async Task StateTransitions()
{
    await using var f = new Fixture();
    await f.Runtime.TickAsync(); Equal(ModeState.Waiting, f.Runtime.View.State);
    f.Platform.Processes.AddRange([Game(), Proc(100105, @"C:\Games\cs2.exe", 400)]);
    await f.Runtime.TickAsync(); Equal(ModeState.Active, f.Runtime.View.State);
    Equal(2, f.Runtime.View.Games.Count); True(f.Runtime.View.LastRuleMatches.Count >= 2);
    f.Runtime.SetEnabled(false); Equal(ModeState.ForcedOff, f.Runtime.View.State);
    await f.Runtime.TickAsync(); Equal(0, f.Platform.Closed.Count);
    f.Runtime.SetEnabled(true);
    f.Platform.Processes.RemoveAll(p => p.FullPath == Game().FullPath);
    await f.Runtime.TickAsync(); Equal(ModeState.Active, f.Runtime.View.State);
    f.Runtime.UpdateSettings(s => s.Games.ForEach(r => r.Enabled = false));
    await f.Runtime.TickAsync(); Equal(ModeState.Waiting, f.Runtime.View.State);
    Equal(0, f.Platform.Closed.Count);
    f.Runtime.SetEnabled(false); f.Platform.Processes.Clear(); await f.Runtime.TickAsync();
    Equal(ModeState.ForcedOff, f.Runtime.View.State);
}

async Task RestoreOnce()
{
    await using var f = new Fixture();
    f.Platform.Processes.AddRange([Game(), Proc(100202, AppPath, 300), Proc(100203, @"C:\TestApps\small.exe", 20)]);
    await f.Runtime.TickAsync();
    Equal(1, f.Platform.Closed.Count); Equal(1, f.Runtime.View.ClosedCount);
    var entry = f.Runtime.View.RestoreQueue.Single(); Equal(RestoreState.Pending, entry.State);
    Equal("--test document.txt", entry.Launch!.Arguments);
    Equal(@"C:\TestApps", entry.Launch.WorkingDirectory); Equal(1, entry.Launch.Windows.Count);
    f.Platform.Processes.RemoveAll(p => p.Id == Game().Id);
    await f.Runtime.TickAsync(); Equal(ModeState.Waiting, f.Runtime.View.State);
    Equal(1, f.Platform.Restarted.Count); Equal(RestoreState.Started, f.Runtime.View.RestoreQueue.Single().State);
    await f.Runtime.TickAsync(); Equal(1, f.Platform.Restarted.Count);
}

async Task PreventDuplicate()
{
    await using var f = await ClosedFixture();
    f.Platform.Processes.Clear(); f.Platform.Processes.Add(Proc(100303, AppPath.ToUpperInvariant(), 20));
    await f.Runtime.TickAsync(); Equal(0, f.Platform.Restarted.Count);
    Equal(RestoreState.Skipped, f.Runtime.View.RestoreQueue.Single().State);
    Contains("уже работает", f.Runtime.View.RestoreQueue.Single().Detail);
}

async Task NewExclusion()
{
    await using var f = await ClosedFixture();
    f.Runtime.UpdateSettings(s => s.Exclusions.Add(Rule(AppPath)));
    f.Platform.Processes.Clear(); await f.Runtime.TickAsync();
    Equal(0, f.Platform.Restarted.Count); Contains("исключения", f.Runtime.View.RestoreQueue.Single().Detail);
}

async Task RestoreSwitches()
{
    await using (var f = await ClosedFixture())
    {
        f.Runtime.SetRestoreEnabled(f.Runtime.View.RestoreQueue.Single().Id, false);
        True(f.Runtime.Settings.NoRestorePaths.Contains(AppPath));
        f.Platform.Processes.Clear(); await f.Runtime.TickAsync();
        Equal(0, f.Platform.Restarted.Count); Contains("отключён", f.Runtime.View.RestoreQueue.Single().Detail);
    }
    await using (var f = await ClosedFixture())
    {
        f.Runtime.UpdateSettings(s => s.RestoreApps = false);
        f.Platform.Processes.Clear(); await f.Runtime.TickAsync(); Equal(0, f.Platform.Restarted.Count);
    }
    await using (var f = await ClosedFixture())
    {
        f.Runtime.SetEnabled(false);
        await f.Runtime.TickAsync(); Equal(0, f.Platform.Restarted.Count);
        f.Platform.Processes.Clear(); await f.Runtime.TickAsync(); Equal(1, f.Platform.Restarted.Count);
    }
}

async Task FailedCloseAndCapture()
{
    await using (var f = new Fixture())
    {
        f.Platform.CaptureAvailable = false;
        f.Platform.Processes.AddRange([Game(), Proc(100202, AppPath, 300)]);
        await f.Runtime.TickAsync(); Equal(1, f.Platform.Closed.Count);
        f.Platform.Processes.Clear(); await f.Runtime.TickAsync();
        Equal(0, f.Platform.Restarted.Count); Contains("данных", f.Runtime.View.RestoreQueue.Single().Detail);
    }
    await using (var f = new Fixture())
    {
        f.Platform.CloseSucceeds = false;
        f.Platform.Processes.AddRange([Game(), Proc(100202, AppPath, 300)]);
        await f.Runtime.TickAsync(); Equal(0, f.Runtime.View.ClosedCount);
        Equal(RestoreState.Skipped, f.Runtime.View.RestoreQueue.Single().State);
        f.Platform.Processes.Clear(); await f.Runtime.TickAsync(); Equal(0, f.Platform.Restarted.Count);
    }
}

async Task RevalidateClose()
{
    await using var f = new Fixture();
    f.Platform.Processes.AddRange([Game(), Proc(100202, AppPath, 300)]);
    f.Platform.BeforeClose = () => f.Platform.Processes.RemoveAll(p => p.Id == Game().Id);
    await f.Runtime.TickAsync(); Equal(1, f.Platform.CloseAttempts.Count); Equal(0, f.Platform.Closed.Count);
    Equal(RestoreState.Skipped, f.Runtime.View.RestoreQueue.Single().State);
}

async Task Persistence()
{
    await using var f = new Fixture();
    f.Runtime.UpdateSettings(s => { s.ThresholdMiB = 250; s.PollSeconds = 15; s.Exclusions.Add(Rule(AppPath)); });
    var loaded = f.Store.Load(); Equal(250, loaded.ThresholdMiB); Equal(15, loaded.PollSeconds);
    Equal(AppPath, loaded.Exclusions.Single().FullPath);
    var snapshot = f.Runtime.Settings; snapshot.ThresholdMiB = 999;
    Equal(250, f.Runtime.Settings.ThresholdMiB);
    File.WriteAllText(Path.Combine(f.Directory, "settings.json"), "{ broken json");
    var safe = f.Store.Load(); False(safe.Enabled); True(f.Store.LoadError is not null);
    True(File.ReadAllText(Path.Combine(f.Directory, "settings.json")).Contains("broken"));
}

async Task RevalidateRestore()
{
    foreach (var change in new[] { "exclude", "disable", "game" })
    {
        await using var f = await ClosedFixture();
        f.Platform.Processes.Clear();
        f.Platform.BeforeRestart = () =>
        {
            if (change == "exclude") f.Runtime.UpdateSettings(s => s.Exclusions.Add(Rule(AppPath)));
            else if (change == "disable") f.Runtime.UpdateSettings(s => s.RestoreApps = false);
            else f.Platform.Processes.Add(Game());
        };
        await f.Runtime.TickAsync();
        Equal(0, f.Platform.Restarted.Count);
        Equal(RestoreState.Failed, f.Runtime.View.RestoreQueue.Single().State);
    }
}

async Task JournalFailure()
{
    await using var f = new Fixture();
    f.Platform.Processes.AddRange([Game(), Proc(100202, AppPath, 300)]);
    using (var locked = new FileStream(Path.Combine(f.Directory, "restart-journal.json.tmp"), FileMode.Create, FileAccess.Write, FileShare.None))
    {
        var failed = false;
        try { await f.Runtime.TickAsync(); }
        catch (IOException) { failed = true; }
        True(failed); Equal(0, f.Platform.CloseAttempts.Count); Equal(0, f.Platform.Closed.Count);
    }
}

async Task InterruptedSession()
{
    await using var f = new Fixture(initializeRuntime: false);
    f.Store.SaveQueue([new RestartEntry { Name = "editor", Path = AppPath, Launch = FakePlatform.Launch(AppPath), State = RestoreState.Pending }]);
    f.Initialize();
    Equal(RestoreState.Interrupted, f.Runtime.View.RestoreQueue.Single().State);
    await f.Runtime.TickAsync(); Equal(0, f.Platform.Restarted.Count);
    Contains("прерван", f.Runtime.View.RestoreQueue.Single().Detail);
}

async Task ReadOnlyModes()
{
    await using (var f = new Fixture())
    {
        f.Platform.Processes.AddRange([Game(), Proc(100202, AppPath, 300)]);
        await f.Runtime.RefreshAsync(); Equal(0, f.Platform.CloseAttempts.Count); Equal(2, f.Runtime.View.Processes.Count);
    }
    await using (var f = new Fixture(preview: true))
    {
        f.Platform.Processes.AddRange([Game(), Proc(100202, AppPath, 300)]);
        await f.Runtime.TickAsync(); Equal(0, f.Platform.CloseAttempts.Count); Equal(0, f.Platform.Scans);
    }
}


async Task HighestMemoryFirst()
{
    await using var f = new Fixture();
    var largest = Proc(100301, @"C:\TestApps\large.exe", 950);
    var medium = Proc(100302, @"C:\TestApps\medium.exe", 460);
    var smallest = Proc(100303, @"C:\TestApps\small.exe", 201);
    var excluded = Proc(100304, @"C:\TestApps\excluded.exe", 1100);
    var critical = Proc(100305, @"C:\TestApps\critical.exe", 2000) with { SafetyVerified = false, ProtectedReason = "Critical" };
    f.Runtime.UpdateSettings(s => s.Exclusions.Add(Rule(excluded.FullPath!)));
    f.Platform.Processes.AddRange([smallest, Game(), excluded, medium, critical, largest]);
    await f.Runtime.TickAsync();
    Equal(string.Join(",", new[] { largest.Id, medium.Id, smallest.Id }), string.Join(",", f.Platform.Closed));
    True(f.Platform.Processes.Any(p => p.Id == excluded.Id));
    True(f.Platform.Processes.Any(p => p.Id == critical.Id));
    True(f.Platform.Processes.Any(p => p.Id == Game().Id));
}

Task QuietNotifications()
{
    var policy = new NotificationPlanner();
    var now = DateTimeOffset.UtcNow;
    Equal<string?>(null, policy.Observe(NoticeView(ModeState.Active, 0, true), true, now));
    Equal<string?>(null, policy.Observe(NoticeView(ModeState.Active, 3, true), true, now.AddSeconds(20)));
    Contains("3", policy.Observe(NoticeView(ModeState.Waiting, 3, false), true, now.AddMinutes(1))!);
    Equal<string?>(null, policy.Observe(NoticeView(ModeState.Waiting, 3, false), true, now.AddMinutes(2)));
    Equal<string?>(null, policy.Error(new(now, "Ошибка", "Repeated background failure"), true, now.AddMinutes(2)));
    Equal<string?>(null, policy.Error(new(now, "Внимание", "A skipped process"), true, now.AddMinutes(12)));
    True(policy.Error(new(now, "Ошибка", "Background failure"), true, now.AddMinutes(12)) != null);
    Equal<string?>(null, policy.Error(new(now, "Ошибка", "Again"), true, now.AddMinutes(13)));
    return Task.CompletedTask;
}
Task DisabledNotifications()
{
    var policy = new NotificationPlanner();
    var now = DateTimeOffset.UtcNow;
    policy.Observe(NoticeView(ModeState.Active, 0, true), false, now);
    Equal<string?>(null, policy.Observe(NoticeView(ModeState.ForcedOff, 2, false), false, now.AddMinutes(1)));
    Equal<string?>(null, policy.Error(new(now, "ERROR", "Error"), false, now.AddMinutes(2)));
    Equal<string?>(null, policy.Observe(NoticeView(ModeState.Waiting, 2, false), true, now.AddMinutes(20)));
    return Task.CompletedTask;
}
RuntimeView NoticeView(ModeState state, int closed, bool game) =>
    new(state, "", game ? [Game()] : [], [], [], [], closed * MiB, closed, new Dictionary<Guid, DateTimeOffset>());

Task ExperimentalGames()
{
    var settings = EnabledSettings();
    var candidate = Proc(100306, @"C:\Steam\steamapps\common\Example\game.exe", 700) with { GameLibraryHint = "Steam · Example" };
    Equal(0, ProcessPolicy.DetectGames(settings, [candidate]).Count);
    False(CanClose(candidate, [Game(), candidate], settings));
    settings.ExperimentalGameDetection = true;
    Equal(1, ProcessPolicy.DetectGames(settings, [candidate]).Count);
    False(CanClose(candidate, [candidate], settings));
    Equal(0, ProcessPolicy.DetectGames(settings, [candidate with { SafetyVerified = false }]).Count);
    Equal(0, ProcessPolicy.DetectGames(settings, [candidate with { HasVisibleWindow = false }]).Count);
    Equal(0, ProcessPolicy.DetectGames(settings, [candidate with { ProtectedReason = "Service" }]).Count);
    settings.Exclusions.Add(Rule(candidate.FullPath!));
    Equal(0, ProcessPolicy.DetectGames(settings, [candidate]).Count);
    settings.Exclusions.Clear();
    var rule = Rule(candidate.FullPath!); rule.Enabled = false; settings.Games.Add(rule);
    Equal(0, ProcessPolicy.DetectGames(settings, [candidate]).Count);
    False(CanClose(candidate, [Game(), candidate], settings));
    return Task.CompletedTask;
}

async Task QolSettingsPersist()
{
    await using var f = new Fixture(initializeRuntime: false);
    var settings = f.Store.Load();
    True(settings.LaunchAtLogin); True(settings.NotificationsEnabled); False(settings.ExperimentalGameDetection);
    settings.LaunchAtLogin = false; settings.NotificationsEnabled = false; settings.ExperimentalGameDetection = true;
    f.Store.Save(settings); var loaded = f.Store.Load();
    False(loaded.LaunchAtLogin); False(loaded.NotificationsEnabled); True(loaded.ExperimentalGameDetection);
    Equal("Beta 0.1.1", AppVersion.Display); Equal(new Version(0, 1, 1), AppVersion.Current);
}
async Task SteamManifestBoundaries()
{
    await using var f = new Fixture(initializeRuntime: false);
    var steam = Path.Combine(f.Directory, "Steam");
    var apps = Path.Combine(steam, "steamapps");
    var game = Path.Combine(apps, "common", "Test Game");
    Directory.CreateDirectory(game);
    Directory.CreateDirectory(Path.Combine(steam, "outside"));
    File.WriteAllText(Path.Combine(apps, "appmanifest_123.acf"), "\"AppState\" { \"name\" \"Test Game\" \"installdir\" \"Test Game\" }");
    File.WriteAllText(Path.Combine(apps, "appmanifest_999.acf"), "\"AppState\" { \"name\" \"Bad Path\" \"installdir\" \"..\\\\..\\\\outside\" }");
    var index = SteamManifestIndex.Read([steam, "not-absolute", "C:\\bad\0path"]);
    Equal("Steam · Test Game", index.Evidence(Path.Combine(game, "game.exe")));
    Equal<string?>(null, index.Evidence(Path.Combine(apps, "common", "Test Game Other", "game.exe")));
    Equal<string?>(null, index.Evidence(Path.Combine(game, "setup.exe")));
    Equal<string?>(null, index.Evidence(Path.Combine(f.Directory, "outside", "game.exe")));
    Equal(1, index.Roots.Count);
}

async Task BatchRestoreOptOut()
{
    await using var f = new Fixture();
    f.Platform.Processes.AddRange([Game(), Proc(100320, @"C:\TestApps\one.exe", 300), Proc(100321, @"C:\TestApps\two.exe", 350)]);
    await f.Runtime.TickAsync();
    var ids = f.Runtime.View.RestoreQueue.Select(e => e.Id).ToArray();
    f.Runtime.SetRestoreEnabled(ids, false);
    True(f.Runtime.View.RestoreQueue.All(e => !e.RestoreEnabled));
    Equal(2, f.Store.Load().NoRestorePaths.Count);
    f.Platform.Processes.RemoveAll(p => p.Id == Game().Id);
    await f.Runtime.TickAsync();
    Equal(0, f.Platform.Restarted.Count);
}

async Task<Fixture> ClosedFixture()
{
    var f = new Fixture();
    f.Platform.Processes.AddRange([Game(), Proc(100202, AppPath, 300)]);
    await f.Runtime.TickAsync(); Equal(1, f.Platform.Closed.Count);
    return f;
}
ProcessInfo Game() => Proc(100101, @"C:\Games\dota2.exe", 400);
ProcessInfo Proc(int id, string path, long mib) => new(id, 638600000000000000 + id, 0, Path.GetFileNameWithoutExtension(path), path, mib * MiB, true, null, true, 1);
ExecutableRule Rule(string path) => new() { Label = Path.GetFileNameWithoutExtension(path), FullPath = path };
AppSettings EnabledSettings() => new() { Enabled = true, RiskAcknowledged = true };
bool CanClose(ProcessInfo process, ProcessInfo[] processes, AppSettings settings) =>
    ProcessPolicy.CanClose(process, new ProcessScan(processes, true), settings, Environment.ProcessId, out _);
void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
void False(bool value) { if (value) throw new InvalidOperationException("Expected false."); }
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
void Contains(string expected, string actual) { if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Expected text '{expected}' in '{actual}'."); }

sealed class Fixture : IAsyncDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "GameMode.Tests", Guid.NewGuid().ToString("N"));
    public SettingsStore Store { get; }
    public FakePlatform Platform { get; } = new();
    public GameModeRuntime Runtime { get; private set; } = null!;
    private readonly bool _preview;
    public Fixture(bool initializeRuntime = true, bool preview = false)
    {
        _preview = preview; Store = new SettingsStore(Directory);
        Store.Save(new AppSettings { Enabled = true, RiskAcknowledged = true });
        if (initializeRuntime) Initialize();
    }
    public void Initialize() => Runtime = new GameModeRuntime(Store, Platform, new FakeLog(), _preview);
    public async ValueTask DisposeAsync()
    {
        if (Runtime is not null) await Runtime.DisposeAsync();
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GameMode.Tests")) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Directory);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test cleanup path.");
        System.IO.Directory.Delete(target, recursive: true);
    }
}

sealed class FakeLog : IGameModeLog
{
    private readonly List<LogEntry> _entries = [];
    public IReadOnlyList<LogEntry> Entries => _entries.ToArray();
    public event Action<LogEntry>? Added;
    public void Write(string level, string message)
    { var entry = new LogEntry(DateTimeOffset.Now, level, message); _entries.Add(entry); Added?.Invoke(entry); }
}

sealed class FakePlatform : IProcessPlatform
{
    public List<ProcessInfo> Processes { get; } = [];
    public List<int> CloseAttempts { get; } = [];
    public List<int> Closed { get; } = [];
    public List<LaunchInfo> Restarted { get; } = [];
    public bool InventoryAvailable { get; set; } = true;
    public bool CaptureAvailable { get; set; } = true;
    public bool CloseSucceeds { get; set; } = true;
    public int Scans { get; private set; }
    public Action? BeforeClose { get; set; }
    public Action? BeforeRestart { get; set; }
    public Task<ProcessScan> ScanAsync(CancellationToken token)
    { token.ThrowIfCancellationRequested(); Scans++; return Task.FromResult(new ProcessScan(Processes.ToArray(), InventoryAvailable, InventoryAvailable ? null : "Test service inventory unavailable")); }
    public Task<RestartCapture> CaptureRestartAsync(ProcessInfo process, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(CaptureAvailable ? new RestartCapture(Launch(process.FullPath!), null) : new RestartCapture(null, "Command line inaccessible")); }
    public async Task<CloseResult> CloseAsync(ProcessInfo process, bool allowForce, Func<ProcessScan, ProcessInfo, bool> authorize, CancellationToken token)
    {
        CloseAttempts.Add(process.Id); BeforeClose?.Invoke(); token.ThrowIfCancellationRequested();
        var scan = await ScanAsync(token);
        var target = Processes.FirstOrDefault(p => p.Identity == process.Identity);
        if (target is null || !authorize(scan, target)) return new(false, false, "Fresh authorization denied");
        if (!CloseSucceeds) return new(false, false, "Close not confirmed");
        Processes.RemoveAll(p => p.Identity == process.Identity); Closed.Add(process.Id);
        return new(true, false, "Closed after normal window request");
    }
    public Task<LaunchResult> RestartAsync(LaunchInfo launch, Func<ProcessScan, bool> authorize, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        BeforeRestart?.Invoke();
        if (!authorize(new ProcessScan(Processes.ToArray(), InventoryAvailable))) return Task.FromResult(new LaunchResult(false, "Fresh restart authorization denied"));
        if (Processes.Any(p => ProcessPolicy.SamePath(p.FullPath, launch.FullPath))) return Task.FromResult(new LaunchResult(false, "Duplicate refused by platform"));
        Restarted.Add(launch);
        Processes.Add(new(100500 + Restarted.Count, 638600000000000000 + Restarted.Count, 0, Path.GetFileNameWithoutExtension(launch.FullPath), launch.FullPath, 20 * 1048576, true, null, true, 1));
        return Task.FromResult(new LaunchResult(true, "New process started minimized"));
    }
    public static LaunchInfo Launch(string path) => new(path, "--test document.txt", @"C:\TestApps", [new WindowInfo("Disposable document", "TestWindow", false)], new string('A', 64));
}
