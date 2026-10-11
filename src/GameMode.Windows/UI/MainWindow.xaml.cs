using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GameMode.Core;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;

namespace GameMode.Windows.UI;

public partial class MainWindow : Window
{
    private readonly GameModeRuntime _runtime;
    private readonly bool _preview;
    private readonly DispatcherTimer _renderTimer;
    private readonly object _renderGate = new();
    private RuntimeView? _queuedView;
    private ModeState? _lastState;
    private bool _allowClose;
    private bool _updating;
    private string? _ruleFingerprint;
    private string? _restoreFingerprint;
    private string? _logFingerprint;
    public event Action? PreviewExitRequested;

    public MainWindow(GameModeRuntime runtime, bool preview = false)
    {
        _runtime = runtime;
        _preview = preview;
        InitializeComponent();
        _renderTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            RuntimeView? pending;
            lock (_renderGate) { pending = _queuedView; _queuedView = null; }
            if (pending != null) Render(pending);
        }, Dispatcher);
        LogLocation.Text = runtime.DataDirectory;
        runtime.Changed += RuntimeChanged;
        Closing += WindowClosing;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && !_preview) Hide(); };
        Closed += (_, _) => { runtime.Changed -= RuntimeChanged; _renderTimer.Stop(); };
        Loaded += (_, _) =>
        {
            if (SystemParameters.ClientAreaAnimation)
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            AttachHoverAnimations(this);
            Render(runtime.View);
            if (!_preview && !runtime.Settings.RiskAcknowledged) ShowFirstRun();
        };
        Render(runtime.View);
        if (_preview) Title = "Game Mode — предварительный просмотр";
    }

    public void ShowSettings()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void AllowExit() { _allowClose = true; Close(); }

    public void SavePreview(string path)
    {
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void RuntimeChanged(RuntimeView view)
    {
        if (Dispatcher.HasShutdownStarted) return;
        lock (_renderGate) _queuedView = view;
    }

    private void Render(RuntimeView view)
    {
        _updating = true;
        var settings = _runtime.Settings;
        if (!ThresholdInput.IsKeyboardFocused) ThresholdInput.Text = settings.ThresholdMiB.ToString(CultureInfo.InvariantCulture);
        if (!IntervalInput.IsKeyboardFocused) IntervalInput.Text = settings.PollSeconds.ToString(CultureInfo.InvariantCulture);
        RestoreCheck.IsChecked = settings.RestoreApps;
        ForceCheck.IsChecked = settings.AllowForceClose;
        HeaderState.Text = StateName(view.State);
        StateTitle.Text = StateName(view.State);
        StateExplanation.Text = view.Explanation;
        ModeButtonLabel.Text = settings.Enabled ? "Отключить Game Mode" : "Включить Game Mode";
        AutomationProperties.SetName(ModeButton, ModeButtonLabel.Text);
        var color = view.State switch { ModeState.Active => "#A29AFF", ModeState.Waiting => "#B1BACA", _ => "#FFB7BE" };
        HeaderDot.Fill = Brush(color);
        StateTitle.Foreground = Brush(color);
        ModeButton.Background = view.State switch
        {
            ModeState.Active => Gradient("#386DEB", "#8B43DD"),
            ModeState.Waiting => Gradient("#3B4965", "#515671"),
            _ => Gradient("#3E67D9", "#7850C7")
        };
        ReleasedMemory.Text = $"{view.ClosedWorkingSetBytes / 1048576d:N0} МиБ";
        ClosedPrograms.Text = view.ClosedCount.ToString(CultureInfo.CurrentCulture);
        if (_lastState != view.State && SystemParameters.ClientAreaAnimation)
            StateTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(160)));
        _lastState = view.State;
        var fingerprint = string.Join("|", settings.Exclusions.Concat(settings.Games).Select(r => $"{r.Id},{r.Label},{r.FullPath},{r.Enabled}")) +
            string.Join("|", view.LastRuleMatches.Select(m => $"{m.Key}:{m.Value:O}"));
        if (fingerprint != _ruleFingerprint)
        {
            _ruleFingerprint = fingerprint;
            RenderRules(ExclusionRows, settings.Exclusions, false, view);
            RenderRules(GameRows, settings.Games, true, view);
            ExclusionCount.Text = $"{settings.Exclusions.Count} правил";
            GameCount.Text = $"{settings.Games.Count} правил";
        }
        var restoreFingerprint = string.Join("|", view.RestoreQueue.Select(e => $"{e.Id},{e.State},{e.RestoreEnabled},{e.Detail}")) + settings.RestoreApps;
        if (restoreFingerprint != _restoreFingerprint)
        {
            _restoreFingerprint = restoreFingerprint;
            RenderRestore(view.RestoreQueue, settings.RestoreApps);
        }
        var logFingerprint = $"{view.Log.Count}:{view.Log.LastOrDefault()?.Time:O}:{view.Log.LastOrDefault()?.Message}";
        if (logFingerprint != _logFingerprint)
        {
            _logFingerprint = logFingerprint;
            RenderLog(view.Log);
        }
        _updating = false;
    }

    private void RenderRules(StackPanel container, IReadOnlyList<ExecutableRule> rules, bool games, RuntimeView view)
    {
        container.Children.Clear();
        if (rules.Count == 0)
        {
            container.Children.Add(Text(games ? "Добавьте игру из запущенных процессов или выберите её .exe." : "Добавьте браузер, рабочие приложения и всё, что должно оставаться открытым.", 13, "#AAB4CC"));
            return;
        }
        foreach (var rule in rules)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var description = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            var title = Text(rule.Label, 14, "#F5F6FF");
            title.FontWeight = FontWeights.SemiBold;
            description.Children.Add(title);
            var path = Text(rule.FullPath ?? $"{rule.ExecutableName} · встроенное правило", 11, "#AAB4CC");
            path.TextTrimming = TextTrimming.CharacterEllipsis;
            path.ToolTip = rule.FullPath ?? rule.ExecutableName;
            description.Children.Add(path);
            if (games)
            {
                var match = view.LastRuleMatches.TryGetValue(rule.Id, out var time) ? $"Обнаружено: {time.LocalDateTime:dd.MM HH:mm:ss}" : "Ещё не обнаружено";
                var matched = Text(rule.Enabled ? match : "Автодетект временно отключён", 10, rule.Enabled && view.LastRuleMatches.ContainsKey(rule.Id) ? "#A6B7FF" : "#AAB4CC");
                matched.Margin = new Thickness(0, 2, 0, 0);
                description.Children.Add(matched);
            }
            grid.Children.Add(description);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (games)
            {
                var enabled = new CheckBox { IsChecked = rule.Enabled, Content = "Детект", ToolTip = "Временно включить или отключить обнаружение этой игры", Margin = new Thickness(0, 0, 10, 0) };
                AutomationProperties.SetName(enabled, $"Автодетект: {rule.Label}");
                enabled.Click += (_, _) => ChangeSettings(s => { var r = s.Games.FirstOrDefault(r => r.Id == rule.Id); if (r != null) r.Enabled = enabled.IsChecked == true; });
                actions.Children.Add(enabled);
            }
            var pathButton = SmallButton("Путь", $"Просмотреть путь: {rule.Label}");
            pathButton.Click += (_, _) => ShowRulePath(rule);
            actions.Children.Add(pathButton);
            var remove = SmallButton("×", $"Удалить правило: {rule.Label}");
            remove.Margin = new Thickness(6, 0, 0, 0);
            remove.FontSize = 19;
            remove.Click += (_, _) => ChangeSettings(s => (games ? s.Games : s.Exclusions).RemoveAll(r => r.Id == rule.Id));
            actions.Children.Add(remove);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
            container.Children.Add(Row(grid));
        }
    }

    private void RenderRestore(IReadOnlyList<RestartEntry> entries, bool restoreEnabled)
    {
        RestoreRows.Children.Clear();
        RestoreTab.Header = $"Возврат приложений · {entries.Count}";
        if (entries.Count == 0)
        {
            RestoreRows.Children.Add(Text("Пока ничего не закрыто. Здесь появятся приложения и сведения о возможности их повторного запуска.", 13, "#AAB4CC"));
            return;
        }
        foreach (var entry in entries.Reverse())
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var information = new StackPanel();
            information.Children.Add(Text($"{entry.Name}  ·  {RestoreStateName(entry.State)}", 14, entry.State == RestoreState.Failed ? "#FFB7BE" : "#F5F6FF"));
            var details = Text($"{entry.Path ?? "Путь недоступен"}\n{entry.Detail}", 11, "#AAB4CC");
            details.TextWrapping = TextWrapping.Wrap;
            information.Children.Add(details);
            var launch = entry.Launch;
            var tooltip = launch == null ? "Безопасные сведения для повторного запуска недоступны." :
                $"Путь: {launch.FullPath}\nАргументы: {launch.Arguments}\nРабочая папка: {launch.WorkingDirectory}\nОкна: {string.Join("; ", launch.Windows.Select(w => w.Title))}\nПовторный запуск свёрнутым; прежнее состояние не восстанавливается.";
            information.ToolTip = tooltip;
            grid.Children.Add(information);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var check = new CheckBox { Content = "Возврат", IsChecked = entry.RestoreEnabled, IsEnabled = launch != null && !_preview, Margin = new Thickness(0, 0, 12, 0), ToolTip = restoreEnabled ? "Разрешить повторный запуск этой программы, в том числе в будущих игровых сеансах" : "Общий возврат отключён. Выбор сохранится для следующего включения." };
            AutomationProperties.SetName(check, $"Повторный запуск: {entry.Name}");
            check.Click += (_, _) =>
            {
                if (_preview) return;
                try { _runtime.SetRestoreEnabled(entry.Id, check.IsChecked == true); }
                catch (Exception exception) { ShowSettingsError(exception); }
            };
            actions.Children.Add(check);
            var detailButton = SmallButton("Сведения", $"Сведения для повторного запуска: {entry.Name}");
            detailButton.Click += (_, _) => MessageBox.Show(this, tooltip, entry.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            actions.Children.Add(detailButton);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
            RestoreRows.Children.Add(Row(grid));
        }
    }

    private void RenderLog(IReadOnlyList<LogEntry> entries)
    {
        LogRows.Children.Clear();
        if (entries.Count == 0) LogRows.Children.Add(Text("События и ошибки появятся здесь. Журнал сохраняется локально.", 13, "#AAB4CC"));
        foreach (var entry in entries.TakeLast(200).Reverse())
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var time = Text($"{entry.Time.LocalDateTime:HH:mm:ss}  {entry.Level}", 11, entry.Level is "Ошибка" or "Внимание" or "ERROR" or "WARN" ? "#FFB7BE" : "#97ACDF");
            grid.Children.Add(time);
            var message = Text(entry.Message, 12, "#D8E0F3");
            message.TextWrapping = TextWrapping.Wrap;
            Grid.SetColumn(message, 1);
            grid.Children.Add(message);
            grid.Margin = new Thickness(0, 0, 0, 7);
            LogRows.Children.Add(grid);
        }
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_preview) return;
        if (!_runtime.Settings.RiskAcknowledged && !ShowFirstRun()) return;
        if (SystemParameters.ClientAreaAnimation)
        {
            ModeButton.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            var scale = new ScaleTransform(1, 1);
            ModeButton.RenderTransform = scale;
            var animation = new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(130));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
        try { _runtime.SetEnabled(!_runtime.Settings.Enabled); }
        catch (Exception exception) { ShowSettingsError(exception); }
    }

    private bool ShowFirstRun()
    {
        var dialog = new RiskDialog { Owner = this };
        if (dialog.ShowDialog() != true) return false;
        return ChangeSettings(s => { s.RiskAcknowledged = true; s.Enabled = false; });
    }

    private void NumericSetting_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ApplyNumericSettings(); e.Handled = true; }
    }

    private void NumericSetting_LostFocus(object sender, RoutedEventArgs e) => ApplyNumericSettings();
    private void ApplyNumericSettings()
    {
        if (_updating || _preview) return;
        if (!int.TryParse(ThresholdInput.Text, out var threshold) || threshold is < 16 or > 1048576 ||
            !int.TryParse(IntervalInput.Text, out var interval) || interval is < 3 or > 120)
        {
            ValidationMessage.Text = "Порог: 16–1 048 576 МиБ. Интервал: 3–120 секунд.";
            return;
        }
        ValidationMessage.Text = "";
        ChangeSettings(s => { s.ThresholdMiB = threshold; s.PollSeconds = interval; });
    }

    private void RestoreCheck_Click(object sender, RoutedEventArgs e) { if (!_updating) ChangeSettings(s => s.RestoreApps = RestoreCheck.IsChecked == true); }
    private void ForceCheck_Click(object sender, RoutedEventArgs e) { if (!_updating) ChangeSettings(s => s.AllowForceClose = ForceCheck.IsChecked == true); }
    private void AddExclusionFile_Click(object sender, RoutedEventArgs e) => AddFileRule(false);
    private void AddGameFile_Click(object sender, RoutedEventArgs e) => AddFileRule(true);
    private async void AddExclusionProcess_Click(object sender, RoutedEventArgs e) => await AddProcessRuleAsync(false);
    private async void AddGameProcess_Click(object sender, RoutedEventArgs e) => await AddProcessRuleAsync(true);

    private void AddFileRule(bool game)
    {
        if (_preview) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = game ? "Выберите исполняемый файл игры" : "Выберите приложение для исключений", Filter = "Исполняемые файлы (*.exe)|*.exe", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                var resolved = GameMode.Windows.Processes.WindowsProcessPlatform.ResolveExecutablePath(dialog.FileName);
                AddRule(resolved, Path.GetFileNameWithoutExtension(resolved), game);
            }
            catch (Exception exception) { MessageBox.Show(this, "Не удалось проверить полный путь к исполняемому файлу: " + exception.Message, "Game Mode", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }

    private async Task AddProcessRuleAsync(bool game)
    {
        if (_preview) return;
        try
        {
            await _runtime.RefreshAsync();
            var picker = new ProcessPicker(_runtime.View.Processes) { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedProcess is { FullPath: not null } process)
                AddRule(process.FullPath, Path.GetFileNameWithoutExtension(process.Name), game);
        }
        catch (Exception exception)
        {
            _runtime.Log.Write("ERROR", $"Не удалось открыть список процессов: {exception.Message}");
            MessageBox.Show(this, exception.Message, "Список процессов", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddRule(string path, string label, bool game)
    {
        path = Path.GetFullPath(path);
        var rules = game ? _runtime.Settings.Games : _runtime.Settings.Exclusions;
        if (rules.Any(r => string.Equals(r.FullPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Этот исполняемый файл уже есть в списке.", "Game Mode", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ChangeSettings(s => (game ? s.Games : s.Exclusions).Add(new ExecutableRule { Label = label, FullPath = path, ExecutableName = Path.GetFileName(path) }));
    }

    private void ShowRulePath(ExecutableRule rule)
    {
        var detectedPaths = _runtime.View.Processes.Where(p => ProcessPolicy.Matches(rule, p) && p.FullPath != null).Select(p => p.FullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var message = rule.FullPath ?? (detectedPaths.Length > 0 ? string.Join("\n", detectedPaths) : $"Встроенное правило: {rule.ExecutableName}\nПроверяется имя исполняемого файла, а не заголовок окна. Полный путь зависит от установки игры и появится после её обнаружения.");
        MessageBox.Show(this, message, rule.Label, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_preview) { UpdateMessage.Text = "Предварительный просмотр: проверка сети отключена."; return; }
        if (sender is not Button button) return;
        button.IsEnabled = false;
        UpdateMessage.Text = "Проверяем источник проекта…";
        try { UpdateMessage.Text = await _runtime.CheckUpdatesAsync(); }
        catch (Exception exception) { UpdateMessage.Text = $"Проверка недоступна: {exception.Message}"; }
        finally { button.IsEnabled = true; }
    }

    private bool ChangeSettings(Action<AppSettings> change)
    {
        if (_preview) return false;
        try { _runtime.UpdateSettings(change); return true; }
        catch (Exception exception) { ShowSettingsError(exception); return false; }
    }

    private void ShowSettingsError(Exception exception)
    {
        _runtime.Log.Write("Ошибка", "Не удалось сохранить настройки: " + exception.Message);
        MessageBox.Show(this, "Настройки не сохранены: " + exception.Message, "Game Mode", MessageBoxButton.OK, MessageBoxImage.Warning);
        Render(_runtime.View);
    }

    private void ShowLicense_Click(object sender, RoutedEventArgs e)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "LICENSE");
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "LICENSE"));
        var licensePath = File.Exists(bundled) ? bundled : source;
        try
        {
            var text = File.Exists(licensePath) ? File.ReadAllText(licensePath) : "GNU General Public License v3\n\nПолный текст лицензии доступен в файле LICENSE репозитория vasilii-dev/game-mode и на https://www.gnu.org/licenses/gpl-3.0.html. Программа предоставляется без гарантий.";
            var viewer = new Window { Title = "Game Mode — GPLv3", Width = 850, Height = 650, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#151D30") };
            viewer.Content = new System.Windows.Controls.TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18), Background = Brush("#0F1526"), Foreground = Brush("#E7ECFB"), FontFamily = new FontFamily("Consolas"), FontSize = 13 };
            viewer.ShowDialog();
        }
        catch (Exception exception) { MessageBox.Show(this, "Не удалось открыть лицензию: " + exception.Message, "Game Mode", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OpenLogDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (_preview) return;
        try
        {
            var launch = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            launch.ArgumentList.Add(_runtime.DataDirectory);
            Process.Start(launch);
        }
        catch (Exception exception) { _runtime.Log.Write("ERROR", $"Не удалось открыть папку журнала: {exception.Message}"); }
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        if (_preview)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (PreviewExitRequested != null) PreviewExitRequested.Invoke();
                else System.Windows.Application.Current?.Shutdown();
            }));
            return;
        }
        e.Cancel = true;
        Hide();
    }

    internal static string StateName(ModeState state) => state switch { ModeState.Active => "Включен", ModeState.Waiting => "Ожидает", _ => "Принудительно выключен" };
    private static string RestoreStateName(RestoreState state) => state switch { RestoreState.Pending => "Ожидает окончания игры", RestoreState.Started => "Запущено свёрнутым", RestoreState.Skipped => "Пропущено", RestoreState.Interrupted => "Предыдущая сессия прервана", _ => "Не удалось запустить" };
    internal static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static LinearGradientBrush Gradient(string first, string second) => new((Color)ColorConverter.ConvertFromString(first), (Color)ColorConverter.ConvertFromString(second), 45);
    internal static TextBlock Text(string value, double size, string color) => new() { Text = value, FontSize = size, Foreground = Brush(color) };
    private static Border Row(UIElement child) => new() { Child = child, Background = Brush("#101728"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 7) };
    private static Button SmallButton(string label, string accessibleName)
    {
        var button = new Button { Content = label, Padding = new Thickness(9, 5, 9, 5), FontSize = 11 };
        AutomationProperties.SetName(button, accessibleName);
        button.ToolTip = accessibleName;
        AttachHover(button);
        return button;
    }

    private static readonly DependencyProperty HoverAttachedProperty = DependencyProperty.RegisterAttached("HoverAttached", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    private static void AttachHoverAnimations(DependencyObject parent)
    {
        if (parent is Button button) AttachHover(button);
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            AttachHoverAnimations(VisualTreeHelper.GetChild(parent, index));
    }
    private static void AttachHover(Button button)
    {
        if ((bool)button.GetValue(HoverAttachedProperty)) return;
        button.SetValue(HoverAttachedProperty, true);
        button.MouseEnter += (_, _) => { if (SystemParameters.ClientAreaAnimation) button.BeginAnimation(OpacityProperty, new DoubleAnimation(0.94, TimeSpan.FromMilliseconds(90))); };
        button.MouseLeave += (_, _) => { if (SystemParameters.ClientAreaAnimation) button.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(110))); };
    }
}
