using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameMode.Core;
using GameMode.Windows.UI;
using Microsoft.Win32;

internal static class UserInterfaceChecks
{
    public static Task<int> RunAsync()
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Run(completion)) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void Run(TaskCompletionSource<int> completion)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GameMode.UiChecks"));
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Application? app = null;
        MainWindow? main = null;
        GameModeRuntime? runtime = null;
        try
        {
            Directory.CreateDirectory(directory);
            using var startupKey = Registry.CurrentUser.OpenSubKey(GameMode.Windows.StartupManager.RunKey);
            var startupBefore = startupKey?.GetValue(GameMode.Windows.StartupManager.ValueName) as string;
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var store = new SettingsStore(directory);
            var settings = new AppSettings { RiskAcknowledged = true, Enabled = false };
            for (var i = 1; i <= 3; i++)
                settings.Exclusions.Add(new() { Label = "Test app " + i, FullPath = Path.Combine(directory, "app" + i + ".exe") });
            store.Save(settings);
            var game = new ProcessInfo(900001, 1, 0, "dota2.exe", @"C:\FakeGames\dota2.exe", 300L * 1048576, true, null, true, 1);
            runtime = new GameModeRuntime(store, new ReadOnlyPlatform(game), new EventLog(directory));
            main = new MainWindow(runtime);
            app.MainWindow = main;
            main.Loaded += async (_, _) =>
            {
                try
                {
                    var bar = Descendants(main).OfType<System.Windows.Controls.Primitives.ScrollBar>().First(s => s.IsVisible && s.Orientation == Orientation.Vertical);
                    bar.ApplyTemplate();
                    var track = (System.Windows.Controls.Primitives.Track)bar.Template.FindName("PART_Track", bar);
                    track.Thumb.ApplyTemplate();
                    Require(track.Thumb.Template.FindName("ThumbSurface", track.Thumb) is Border, "Dark custom scrollbar used");
                    var label = (TextBlock)main.FindName("VersionLabel");
                    Require(label.Text == "Beta 0.1", "Beta version shown on main screen");
                    var exclusions = (ListBox)main.FindName("ExclusionRows");
                    exclusions.SelectedItems.Add(exclusions.Items[0]); exclusions.SelectedItems.Add(exclusions.Items[1]);
                    Require(exclusions.SelectedItems.Count == 2, "Rules support multiple selection");
                    runtime.UpdateSettings(s => s.PollSeconds = 11);
                    await Task.Delay(330);
                    Require(exclusions.SelectedItems.Count == 2, "Selection survives settings refresh");
                    ((Button)main.FindName("RemoveSelectedExclusions")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(330);
                    Require(runtime.Settings.Exclusions.Count == 1 && exclusions.Items.Count == 1, "Selected rules removed together");

                    var games = (ListBox)main.FindName("GameRows");
                    games.SelectedItems.Add(games.Items[0]); games.SelectedItems.Add(games.Items[1]);
                    await runtime.RefreshAsync(); await Task.Delay(330);
                    Require(games.SelectedItems.Count == 2, "Selection survives detected executable refresh");
                    var navigation = (FrameworkElement)main.FindName("HeaderNavigation");
                    main.UpdateLayout();
                    var before = navigation.TransformToAncestor((Visual)main.Content).Transform(new Point()).X;
                    runtime.SetEnabled(true); await Task.Delay(330); main.UpdateLayout();
                    var after = navigation.TransformToAncestor((Visual)main.Content).Transform(new Point()).X;
                    Require(Math.Abs(before - after) < 0.1, "Navigation stays centered when status text changes");
                    runtime.SetEnabled(false); await Task.Delay(330);

                    var box = (CheckBox)main.FindName("ForceCheck");
                    box.ApplyTemplate();
                    Require(box.Template.FindName("CheckMark", box) is System.Windows.Shapes.Path, "Custom checkbox template used");
                    box.IsChecked = false; await Task.Delay(230);
                    var mark = (System.Windows.Shapes.Path)box.Template.FindName("CheckMark", box);
                    Require(Math.Abs(mark.StrokeDashOffset - 20) < 0.1, "Unchecked mark hidden");
                    box.IsChecked = true; await Task.Delay(230);
                    Require(Math.Abs(mark.StrokeDashOffset) < 0.1, "Checkmark drawing finishes");

                    var appExe = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "src", "GameMode.Windows", "bin", "Release", "net10.0-windows", "GameMode.exe"));
                    Require(await ProcessIcons.LoadAsync(appExe) != null, "Real executable icon extracted");
                    var noIcon = Path.Combine(directory, "without-icon.exe"); File.WriteAllText(noIcon, "not a PE image");
                    Require(await ProcessIcons.LoadAsync(noIcon) == null, "Missing icon receives no substitute");
                    var entries = new[]
                    {
                        new ProcessInfo(900010, 1, 0, "alpha.exe", appExe, 600L * 1048576, true, null, true, 1),
                        new ProcessInfo(900011, 1, 0, "beta.exe", noIcon, 300L * 1048576, true, null, true, 1),
                        new ProcessInfo(900012, 1, 0, "alpha-child.exe", appExe, 100L * 1048576, true, null, true, 1)
                    };
                    var picker = new ProcessPicker(entries) { Owner = main };
                    picker.Loaded += (_, _) =>
                    {
                        var list = (ListBox)picker.FindName("ProcessList");
                        list.SelectedItems.Add(list.Items[0]); list.SelectedItems.Add(list.Items[1]);
                        ((TextBox)picker.FindName("ProcessSearch")).Text = "alpha";
                        Require(((Button)picker.FindName("AddSelectedProcesses")).IsEnabled, "Hidden filtered selection retained");
                        ((Button)picker.FindName("AddSelectedProcesses")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    };
                    Require(picker.ShowDialog() == true && picker.SelectedProcesses.Count == 2, "Multiple processes added across search filters");

                    var scroll = new ScrollViewer { Content = new Border { Height = 2000 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                    SmoothScrolling.SetEnabled(scroll, true);
                    var scrollWindow = new Window { Width = 400, Height = 200, Content = scroll, Owner = main, ShowInTaskbar = false };
                    scrollWindow.Show(); scrollWindow.UpdateLayout();
                    scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent, Source = scroll });
                    await Task.Delay(330);
                    var settled = scroll.VerticalOffset;
                    await Task.Delay(200);
                    Require(SystemParameters.WheelScrollLines == 0 || settled > 0, "Wheel scroll moves content");
                    Require(Math.Abs(scroll.VerticalOffset - settled) < 0.1, "Smooth scroll settles without jumping back");
                    scrollWindow.Close();

                    var authors = main.ShowAuthors();
                    authors.UpdateLayout();
                    Require(Descendants(authors).OfType<TextBlock>().Any(t => t.Text == "vasilii-dev"), "Author identity retained");
                    Require(!Descendants(authors).OfType<TextBlock>().Any(t => t.Text.Contains("Идея Game Mode")), "Author description removed");
                    authors.Close();
                    using var startupAfterKey = Registry.CurrentUser.OpenSubKey(GameMode.Windows.StartupManager.RunKey);
                    Require(startupBefore == startupAfterKey?.GetValue(GameMode.Windows.StartupManager.ValueName) as string, "UI tests do not modify Windows startup");
                    await runtime.DisposeAsync();
                    main.AllowExit();
                    completion.TrySetResult(0);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("FAIL UI: " + ex);
                    completion.TrySetResult(1);
                    main.AllowExit();
                }
                finally { app.Shutdown(); }
            };
            app.Run(main);
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL UI initialization: " + ex); completion.TrySetResult(1); }
        finally
        {
            if (Path.GetFullPath(directory).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in Descendants(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("PASS UI " + description);
    }
    private sealed class ReadOnlyPlatform(ProcessInfo game) : IProcessPlatform
    {
        public Task<ProcessScan> ScanAsync(CancellationToken token) => Task.FromResult(new ProcessScan([game], true));
        public Task<RestartCapture> CaptureRestartAsync(ProcessInfo process, CancellationToken token) => throw new InvalidOperationException("UI test cannot capture or close a process");
        public Task<CloseResult> CloseAsync(ProcessInfo process, bool force, Func<ProcessScan, ProcessInfo, bool> authorize, CancellationToken token) => throw new InvalidOperationException("UI test cannot close a process");
        public Task<LaunchResult> RestartAsync(LaunchInfo launch, Func<ProcessScan, bool> authorize, CancellationToken token) => throw new InvalidOperationException("UI test cannot launch a process");
    }
}
