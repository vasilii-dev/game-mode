using System.IO;
using System.Windows;
using GameMode.Core;
using GameMode.Windows.Processes;
using GameMode.Windows.UI;
using GameMode.Windows.Tray;

namespace GameMode.Windows;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var preview = args.Contains("--preview", StringComparer.OrdinalIgnoreCase);
        if (!preview && args.Contains("--register-startup", StringComparer.OrdinalIgnoreCase))
        { if (StartupManager.Apply(true) != null) Environment.ExitCode = 1; return; }
        using var singleInstance = new Mutex(true, preview ? "Local\\GameMode-Preview-" + Guid.NewGuid().ToString("N") : "Local\\GameMode-" + Environment.UserName, out var ownsMutex);
        if (!ownsMutex && !preview) { if (!args.Contains("--startup")) System.Windows.MessageBox.Show("Game Mode уже работает. Откройте настройки через значок в трее.", "Game Mode"); return; }
        var dataDirectory = preview ? Path.Combine(Path.GetTempPath(), "GameMode-Preview-" + Guid.NewGuid().ToString("N")) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameMode");
        try
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Theme.Apply(app);
            var store = new SettingsStore(dataDirectory);
            var log = new EventLog(dataDirectory);
            var runtime = new GameModeRuntime(store, new WindowsProcessPlatform(), log, preview);
            bool? lastStartupSetting = null;
            void SyncStartup(RuntimeView _)
            {
                if (preview || !runtime.Settings.RiskAcknowledged) return;
                var enabled = runtime.Settings.LaunchAtLogin;
                if (lastStartupSetting == enabled) return;
                lastStartupSetting = enabled;
                var error = StartupManager.Apply(enabled);
                log.Write(error == null ? "Настройки" : "Ошибка", error ?? StartupManager.Status);
            }
            runtime.Changed += SyncStartup;
            SyncStartup(runtime.View);
            var window = new MainWindow(runtime, preview);
            app.MainWindow = window;
            void ShowWindow() { window.Show(); if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate(); }
            var exiting = false;
            async void Exit()
            {
                if (exiting) return; exiting = true;
                runtime.Changed -= SyncStartup;
                try { await runtime.DisposeAsync(); }
                catch (Exception ex) { log.Write("Ошибка", "Ошибка выхода: " + ex.Message); }
                window.AllowExit();
                app.Shutdown();
            }
            window.PreviewExitRequested += Exit;
            using var tray = preview ? null : new TrayService(runtime, ShowWindow, Exit, () => !window.IsVisible);
            app.DispatcherUnhandledException += (_, eventArgs) =>
            {
                log.Write("Ошибка", "Ошибка интерфейса: " + eventArgs.Exception.Message);
                try { runtime.SetEnabled(false); } catch { }
                System.Windows.MessageBox.Show("Действие не выполнено. Game Mode отключён; подробности в журнале.\n" + eventArgs.Exception.Message, "Game Mode");
                eventArgs.Handled = true;
            };
            app.SessionEnding += (_, _) => { exiting = true; runtime.DisposeAsync().AsTask().GetAwaiter().GetResult(); };
            window.Loaded += async (_, _) =>
            {
                if (preview)
                {
                    var stateIndex = Array.IndexOf(args, "--preview-state");
                    if (stateIndex >= 0 && stateIndex + 1 < args.Length)
                    {
                        var state = args[stateIndex + 1].ToLowerInvariant() switch
                        {
                            "enabled" or "active" => ModeState.Active,
                            "waiting" => ModeState.Waiting,
                            "off" => ModeState.ForcedOff,
                            _ => throw new ArgumentException("Preview state must be enabled, waiting or off.")
                        };
                        window.ShowPreviewState(state);
                    }
                    var previewWindowIndex = Array.IndexOf(args, "--preview-window");
                    var previewWindow = previewWindowIndex >= 0 && previewWindowIndex + 1 < args.Length ? args[previewWindowIndex + 1] : "main";
                    if (previewWindow == "advanced") window.ShowAdvancedPreview();
                    Window? secondary = previewWindow switch
                    {
                        "authors" => window.ShowAuthors(),
                        "warning" => new RiskDialog(standalonePreview: true) { Owner = window },
                        "processes" => new ProcessPicker((await new WindowsProcessPlatform().ScanAsync(CancellationToken.None)).Processes, standalonePreview: true) { Owner = window },
                        "advanced" => null,
                        "main" => null,
                        _ => throw new ArgumentException("Preview window must be main, authors or warning.")
                    };
                    if (secondary is RiskDialog or ProcessPicker) secondary.Show();
                    var screenshotIndex = Array.IndexOf(args, "--screenshot");
                    if (screenshotIndex >= 0 && screenshotIndex + 1 < args.Length)
                    {
                        await Task.Delay(400);
                        BrandAssets.SavePreview(secondary ?? window, Path.GetFullPath(args[screenshotIndex + 1]));
                        Exit();
                    }
                    return;
                }
                runtime.Start();
            };
            if (preview || !runtime.Settings.RiskAcknowledged || args.Contains("--settings")) ShowWindow();
            else runtime.Start();
            app.Run();
        }
        catch (Exception ex) { System.Windows.MessageBox.Show("Game Mode не удалось запустить:\n" + ex.Message, "Game Mode", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { if (ownsMutex) singleInstance.ReleaseMutex(); }
    }
}
