using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameMode.TestTarget;

public partial class App : Application
{
    private byte[]? _memory;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var requested = e.Args.FirstOrDefault(a => a.StartsWith("--mib=", StringComparison.Ordinal));
        var mib = requested is not null && int.TryParse(requested[6..], out var parsed)
            ? Math.Clamp(parsed, 16, 2048) : 240;
        var ignoreClose = e.Args.Contains("--ignore-close", StringComparer.Ordinal);
        try
        {
            _memory = new byte[checked(mib * 1048576)];
            // Commit physical pages: allocation alone would not create the intended Working Set.
            for (var offset = 0; offset < _memory.Length; offset += Environment.SystemPageSize)
                _memory[offset] = 1;
        }
        catch (Exception ex) when (ex is OutOfMemoryException or OverflowException)
        {
            MessageBox.Show($"Не удалось выделить память: {ex.Message}", "Game Mode — тест");
            Shutdown(1);
            return;
        }

        var body = new StackPanel { Margin = new Thickness(28) };
        body.Children.Add(new TextBlock
        {
            Text = "Одноразовое тестовое приложение",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = $"Выделено и заполнено: {mib} МиБ\nPID: {Environment.ProcessId}\n" +
                "Несохранённых данных нет. Можно безопасно закрыть для проверки.\n\n" +
                (ignoreClose ? "Обычный запрос закрытия игнорируется (--ignore-close)." :
                    "Обычный запрос закрытия разрешён.") +
                "\nПоказанный Working Set может отличаться из-за работы Windows.",
            Margin = new Thickness(0, 20, 0, 20),
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap
        });
        var workingSet = new TextBlock { FontSize = 16 };
        body.Children.Add(workingSet);
        var close = new Button { Content = "Закрыть тестовое приложение", Padding = new Thickness(14),
            Margin = new Thickness(0, 24, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        body.Children.Add(close);
        var allowClose = !ignoreClose;
        var window = new Window
        {
            Title = "GameMode.TestTarget — безопасная проверка",
            Width = 600,
            Height = 410,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(24, 27, 39)),
            Foreground = Brushes.White,
            Content = body
        };
        window.Closing += (_, args) => args.Cancel = !allowClose;
        close.Click += (_, _) => { allowClose = true; window.Close(); };
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            using var process = Process.GetCurrentProcess();
            workingSet.Text = $"Working Set: {process.WorkingSet64 / 1048576d:F1} МиБ";
            GC.KeepAlive(_memory);
        };
        timer.Start();
        window.Closed += (_, _) => timer.Stop();
        window.Show();
    }
}
