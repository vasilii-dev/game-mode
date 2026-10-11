using System.Diagnostics;
using System.Runtime.InteropServices;
using GameMode.Core;
using GameMode.Windows.Processes;

// This executable never runs the production cleaner. Mutation is restricted to the explicit disposable fixture PID.
var platform = new WindowsProcessPlatform();
var scan = await platform.ScanAsync(CancellationToken.None);
Console.WriteLine($"Inventory available: {scan.SafetyInventoryAvailable}; processes: {scan.Processes.Count}; verified GUI apps: {scan.Processes.Count(p => p.SafetyVerified)}");
if (scan.Error is not null) Console.WriteLine(scan.Error);
var own = scan.Processes.SingleOrDefault(p => p.Id == Environment.ProcessId);
Require(own?.SafetyVerified == false, "Windows platform protects its own process");
Require(scan.Processes.Where(p => p.Id <= 4 || p.SessionId == 0).All(p => !p.SafetyVerified), "System/session zero protected");
if (args.Length == 0 || args[0] == "inspect") return 0;
if (args.Length != 3 || args[0] != "exercise" || !int.TryParse(args[1], out var fixturePid)) throw new ArgumentException("Use inspect or exercise <fixture PID> <exact fixture exe>");
var fixturePath = WindowsProcessPlatform.ResolveExecutablePath(args[2]);
var expectedRoot = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "tests", "GameMode.TestTarget", "bin"));
Require(fixturePath.StartsWith(expectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(fixturePath) == "GameMode.TestTarget.exe", "Only the repository disposable fixture is eligible");
Require(scan.SafetyInventoryAvailable, "Mandatory native inventories available");
var fixture = scan.Processes.Single(p => p.Id == fixturePid);
Require(ProcessPolicy.SamePath(fixturePath, fixture.FullPath) && fixture.SafetyVerified, "Exact fixture identity is eligible");
Require(fixture.WorkingSetBytes > 200L * 1048576, "Actual native Working Set exceeds 200 MiB");
var settings = new AppSettings { Enabled = true, RiskAcknowledged = true };
var game = new ProcessInfo(2000000000, 1, 0, "ControlledGame", @"C:\ControlledFixture\game.exe", 300L * 1048576, true, null, true, fixture.SessionId);
settings.Games.Add(new() { Label = "Controlled game", FullPath = game.FullPath });
var policyScan = new ProcessScan(scan.Processes.Concat([game]).ToArray(), true);
Require(ProcessPolicy.CanClose(fixture, policyScan, settings, Environment.ProcessId, out _), "Threshold candidate accepted");
settings.Exclusions.Add(new() { Label = "Fixture", FullPath = fixturePath });
Require(!ProcessPolicy.CanClose(fixture, policyScan, settings, Environment.ProcessId, out _), "Exclusion denies native candidate");
settings.Exclusions.Clear(); settings.Games.Add(new() { Label = "Fixture as game", FullPath = fixturePath });
Require(!ProcessPolicy.CanClose(fixture, policyScan, settings, Environment.ProcessId, out _), "Game denies native candidate");
var capture = await platform.CaptureRestartAsync(fixture, CancellationToken.None);
Require(capture.Launch is not null, "Native capture obtains exact arguments/cwd/windows/hash: " + capture.Reason);
Console.WriteLine($"Captured {capture.Launch!.Windows.Count} windows; launch metadata is not printed.");
var result = await platform.CloseAsync(fixture, true, (fresh, target) => fresh.SafetyInventoryAvailable && target.Identity == fixture.Identity &&
    ProcessPolicy.SamePath(target.FullPath, fixturePath) && target.SafetyVerified && target.ProtectedReason is null, CancellationToken.None);
Require(result.Closed, "Native close confirmed: " + result.Detail);
Console.WriteLine("Forced close: " + result.Forced);
var launch = await platform.RestartAsync(capture.Launch, fresh => fresh.SafetyInventoryAvailable && !fresh.Processes.Any(p => ProcessPolicy.SamePath(p.FullPath, fixturePath)), CancellationToken.None);
Require(launch.Started, "Minimized native relaunch requested: " + launch.Detail);
using var restored = Process.GetProcessesByName("GameMode.TestTarget").First(p => p.MainModule?.FileName is { } path && ProcessPolicy.SamePath(path, fixturePath));
restored.Refresh();
Require(restored.MainWindowHandle != 0 && IsIconic(restored.MainWindowHandle), "Relaunched fixture window is actually minimized");
var duplicate = await platform.RestartAsync(capture.Launch, _ => true, CancellationToken.None);
Require(!duplicate.Started && duplicate.Detail.Contains("дубликат", StringComparison.OrdinalIgnoreCase), "Native duplicate check denies second launch");
var modified = capture.Launch with { ExecutableSha256 = new string('0', 64) };
var changed = await platform.RestartAsync(modified, _ => true, CancellationToken.None);
Require(!changed.Started && changed.Detail.Contains("SHA-256", StringComparison.OrdinalIgnoreCase), "Changed executable hash prevents relaunch");
// Clean up only the exact process just created by this fixture exercise, after all checks are recorded.
if (!restored.HasExited)
{
    restored.CloseMainWindow();
    if (!await WaitForExit(restored, 1500)) restored.Kill(false);
    await restored.WaitForExitAsync();
}
Console.WriteLine("Windows fixture exercise passed. No user applications were targeted.");
return 0;

static void Require(bool condition, string description) { if (!condition) throw new InvalidOperationException("FAIL " + description); Console.WriteLine("PASS " + description); }
static async Task<bool> WaitForExit(Process process, int milliseconds) { using var timeout = new CancellationTokenSource(milliseconds); try { await process.WaitForExitAsync(timeout.Token); return true; } catch (OperationCanceledException) { return false; } }
[DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool IsIconic(nint window);
