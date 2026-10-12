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
            var appExe = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "src", "GameMode.Windows", "bin", "Release", "net10.0-windows", "GameMode.exe"));
            var gameExe = Path.Combine(directory, "dota2.exe");
            File.Copy(appExe, gameExe); // Read its PE icon only; the copied executable is never launched.
            var store = new SettingsStore(directory);
            var settings = new AppSettings { RiskAcknowledged = true, Enabled = false };
            settings.Games.Add(new() { Label = "Custom game", FullPath = appExe });
            for (var i = 1; i <= 3; i++)
                settings.Exclusions.Add(new() { Label = "Test app " + i, FullPath = Path.Combine(directory, "app" + i + ".exe") });
            store.Save(settings);
            var game = new ProcessInfo(900001, 1, 0, "dota2.exe", gameExe, 300L * 1048576, true, null, true, 1);
            var platform = new ReadOnlyPlatform(game);
            runtime = new GameModeRuntime(store, platform, new EventLog(directory));
            main = new MainWindow(runtime);
            app.MainWindow = main;
            main.Loaded += async (_, _) =>
            {
                try
                {
                    CheckScrollbars(main);
                    Require(Descendants(main).OfType<Control>().Where(c => c is Button or CheckBox or TextBox or ListBoxItem or TabItem).All(c => c.FocusVisualStyle == null), "System focus adorners replaced across main controls");
                    var label = (TextBlock)main.FindName("VersionLabel");
                    Require(label.Text == "Beta 0.1.1", "Beta version shown on main screen");
                    var input = (TextBox)main.FindName("ThresholdInput");
                    Require(input.ActualHeight == 40 && ((Border)input.Template.FindName("InputSurface", input)).CornerRadius.TopLeft == 12, "Memory field is compact and rounded");
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
                    var dotaRow = games.Items.Cast<ListBoxItem>().First(i => ((ExecutableRule)i.Tag).ExecutableName == "dota2.exe");
                    var customRow = games.Items.Cast<ListBoxItem>().First(i => ((ExecutableRule)i.Tag).Label == "Custom game");
                    await ProcessIcons.LoadAsync(gameExe); await ProcessIcons.LoadAsync(appExe); await Task.Delay(100);
                    Require(Descendants(dotaRow).OfType<Image>().Any(i => i.Source != null && i.IsVisible) && Descendants(customRow).OfType<Image>().Any(i => i.Source != null), "Built-in and custom game rows display real executable icons");
                    var detection = Descendants(dotaRow).OfType<CheckBox>().Single();
                    detection.IsChecked = false;
                    detection.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(330);
                    Require(ReferenceEquals(dotaRow, games.Items.Cast<ListBoxItem>().First(i => ((ExecutableRule)i.Tag).ExecutableName == "dota2.exe")) && runtime.Settings.Games.First(r => r.ExecutableName == "dota2.exe").Enabled == false, "Detection toggle persists without replacing the animated checkbox");
                    detection.IsChecked = true; detection.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    platform.Processes = []; await runtime.RefreshAsync(); await Task.Delay(330);
                    Require(ReferenceEquals(dotaRow, games.Items.Cast<ListBoxItem>().First(i => ((ExecutableRule)i.Tag).ExecutableName == "dota2.exe")) && Descendants(dotaRow).OfType<Image>().Any(i => i.Source != null), "Discovered game icon survives game exit");
                    platform.Processes = [game]; await runtime.RefreshAsync(); await Task.Delay(330);
                    var navigation = (FrameworkElement)main.FindName("HeaderNavigation");
                    main.UpdateLayout();
                    var before = navigation.TransformToAncestor((Visual)main.Content).Transform(new Point()).X;
                    runtime.SetEnabled(true); await Task.Delay(330); main.UpdateLayout();
                    var after = navigation.TransformToAncestor((Visual)main.Content).Transform(new Point()).X;
                    Require(Math.Abs(before - after) < 0.1, "Navigation stays centered when status text changes");
                    runtime.SetEnabled(false); await Task.Delay(330);
                    foreach (var state in Enum.GetValues<ModeState>())
                    {
                        var preview = new MainWindow(runtime, preview: true);
                        preview.ShowPreviewState(state);
                        Require(((Button)preview.FindName("ModeButton")).Background is SolidColorBrush, "Mode button uses a solid surface: " + state);
                        preview.AllowExit();
                    }

                    var box = (CheckBox)main.FindName("ForceCheck");
                    box.ApplyTemplate();
                    Require(box.Template.FindName("CheckMark", box) is System.Windows.Shapes.Path, "Custom checkbox template used");
                    Require(box.Content is TextBlock wrapped && wrapped.ActualWidth <= box.ActualWidth - 26.0 + 0.1, "Long checkbox label fits inside the mode panel");
                    box.IsChecked = false; await Task.Delay(230);
                    var mark = (System.Windows.Shapes.Path)box.Template.FindName("CheckMark", box);
                    var hidden = mark.StrokeDashArray[0];
                    Require(hidden is > 5 and < 10 && Math.Abs(mark.StrokeDashOffset - hidden) < 0.1, "Unchecked mark completely hidden");
                    Require(((Border)box.Template.FindName("Box", box)).Width == 18 && mark.StrokeThickness == 1.6, "Checkmark is smaller with a thinner stroke");
                    box.IsChecked = true;
                    await Task.Delay(50);
                    Require(!SystemParameters.ClientAreaAnimation || mark.StrokeDashOffset > 0 && mark.StrokeDashOffset < hidden, "Enabling checkbox visibly draws an intermediate checkmark");
                    await Task.Delay(230);
                    Require(Math.Abs(mark.StrokeDashOffset) < 0.1, "Checkmark drawing finishes");
                    box.IsChecked = false;
                    await Task.Delay(50);
                    Require(!SystemParameters.ClientAreaAnimation || mark.StrokeDashOffset > 0 && mark.StrokeDashOffset < hidden, "Disabling checkbox also animates");
                    box.IsChecked = true; await Task.Delay(230);
                    Require(Math.Abs(mark.StrokeDashOffset) < 0.1, "Rapid toggle settles in the final checked state");

                    Require(await ProcessIcons.LoadAsync(appExe) != null, "Real executable icon extracted");
                    var installed = Path.Combine(directory, "installed", "game", "bin", "win64");
                    Directory.CreateDirectory(installed); File.Copy(appExe, Path.Combine(installed, "cs2.exe"));
                    Require(InstalledGamePaths.FindSteamExecutable([Path.Combine(directory, "installed")], "cs2.exe") == Path.Combine(installed, "cs2.exe"), "Installed game icon lookup finds the exact executable without launching it");
                    Require(InstalledGamePaths.FindSteamExecutable([Path.Combine(directory, "installed")], "dota2.exe") == null && InstalledGamePaths.FindSteamExecutable([Path.Combine(directory, "installed")], "../cs2.exe") == null, "Icon lookup does not substitute missing or arbitrary executables");
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
                        CheckScrollbars(picker);
                        var list = (ListBox)picker.FindName("ProcessList");
                        list.SelectedItems.Add(list.Items[0]); list.SelectedItems.Add(list.Items[1]);
                        ((TextBox)picker.FindName("ProcessSearch")).Text = "alpha";
                        Require(((Button)picker.FindName("AddSelectedProcesses")).IsEnabled, "Hidden filtered selection retained");
                        ((Button)picker.FindName("AddSelectedProcesses")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    };
                    Require(picker.ShowDialog() == true && picker.SelectedProcesses.Count == 2, "Multiple processes added across search filters");

                    var scroll = new ScrollViewer { Content = new Border { Height = 2000, Width = 2000 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
                    SmoothScrolling.SetEnabled(scroll, true);
                    var scrollWindow = new Window { Width = 400, Height = 200, Content = scroll, Owner = main, ShowInTaskbar = false };
                    scrollWindow.Show(); scrollWindow.UpdateLayout();
                    CheckScrollbars(scrollWindow);
                    var verticalBar = Descendants(scrollWindow).OfType<System.Windows.Controls.Primitives.ScrollBar>().Single(b => b.Orientation == Orientation.Vertical);
                    System.Windows.Controls.Primitives.ScrollBar.PageDownCommand.Execute(null, verticalBar);
                    await Task.Delay(60);
                    Require(scroll.VerticalOffset > 0, "Custom scrollbar page command scrolls content");
                    scroll.ScrollToVerticalOffset(0); await Task.Delay(30);
                    scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent, Source = scroll });
                    await Task.Delay(330);
                    var settled = scroll.VerticalOffset;
                    await Task.Delay(200);
                    Require(SystemParameters.WheelScrollLines == 0 || settled > 0, "Wheel scroll moves content");
                    Require(Math.Abs(scroll.VerticalOffset - settled) < 0.1, "Smooth scroll settles without jumping back");
                    scrollWindow.Close();

                    var authors = main.ShowAuthors();
                    authors.UpdateLayout();
                    CheckScrollbars(authors);
                    Require(Descendants(authors).OfType<TextBlock>().Any(t => t.Text == "vasilii-dev"), "Author identity retained");
                    Require(!Descendants(authors).OfType<TextBlock>().Any(t => t.Text.Contains("Идея Game Mode")), "Author description removed");
                    authors.Close();
                    var warning = new RiskDialog(requireConsent: false, standalonePreview: true) { Owner = main };
                    warning.Show(); warning.UpdateLayout(); CheckScrollbars(warning); warning.Close();
                    var document = new Window { Owner = main, Width = 400, Height = 200, Content = new TextBox { IsReadOnly = true, Text = string.Join('\n', Enumerable.Repeat(new string('a', 100), 80)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto } };
                    document.Show(); document.UpdateLayout(); CheckScrollbars(document); document.Close();
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
    private static void CheckScrollbars(Window window)
    {
        var bars = Descendants(window).OfType<System.Windows.Controls.Primitives.ScrollBar>().ToArray();
        Require(bars.Length > 0 && bars.All(bar =>
        {
            bar.ApplyTemplate();
            if (bar.Template.FindName("PART_Track", bar) is not System.Windows.Controls.Primitives.Track track) return false;
            track.Thumb.ApplyTemplate();
            if (bar.IsVisible && (bar.Orientation == Orientation.Vertical ? track.Thumb.ActualWidth > 6.1 : track.Thumb.ActualHeight > 6.1))
                Console.Error.WriteLine($"Scrollbar geometry: orientation={bar.Orientation}; bar={bar.ActualWidth}x{bar.ActualHeight}; minimum={bar.MinWidth}x{bar.MinHeight}; thumb={track.Thumb.ActualWidth}x{track.Thumb.ActualHeight}");
            return track.Thumb.Template.FindName("ThumbSurface", track.Thumb) is Border surface && surface.CornerRadius.TopLeft == 3 && (bar.Orientation == Orientation.Vertical ? bar.Width == 8 && (!bar.IsVisible || track.Thumb.ActualWidth <= 6.1) : bar.Height == 8 && (!bar.IsVisible || track.Thumb.ActualHeight <= 6.1));
        }), "Slim custom scrollbars throughout " + window.Title);
    }
    private sealed class ReadOnlyPlatform(ProcessInfo game) : IProcessPlatform
    {
        public ProcessInfo[] Processes { get; set; } = [game];
        public Task<ProcessScan> ScanAsync(CancellationToken token) => Task.FromResult(new ProcessScan(Processes, true));
        public Task<RestartCapture> CaptureRestartAsync(ProcessInfo process, CancellationToken token) => throw new InvalidOperationException("UI test cannot capture or close a process");
        public Task<CloseResult> CloseAsync(ProcessInfo process, bool force, Func<ProcessScan, ProcessInfo, bool> authorize, CancellationToken token) => throw new InvalidOperationException("UI test cannot close a process");
        public Task<LaunchResult> RestartAsync(LaunchInfo launch, Func<ProcessScan, bool> authorize, CancellationToken token) => throw new InvalidOperationException("UI test cannot launch a process");
    }
}
