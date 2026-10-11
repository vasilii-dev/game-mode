using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using GameMode.Core;
using GameMode.Windows.UI;
using Forms = System.Windows.Forms;

namespace GameMode.Windows.Tray;

public sealed class TrayService : IDisposable
{
    private readonly GameModeRuntime _runtime;
    private readonly Dispatcher _dispatcher;
    private readonly Forms.NotifyIcon _notify;
    private readonly Forms.ToolStripMenuItem _status;
    private readonly Forms.ToolStripMenuItem _updates;
    private readonly Dictionary<ModeState, Icon> _icons = [];
    private readonly DispatcherTimer _notificationTimer;
    private readonly List<LogEntry> _pending = [];
    private DateTimeOffset _lastNotification = DateTimeOffset.MinValue;
    private bool _disposed;
    private ModeState? _lastState;

    public TrayService(GameModeRuntime runtime, Action show, Action exit)
    {
        _runtime = runtime;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _icons[ModeState.Active] = LoadIcon("state-active.ico");
        _icons[ModeState.Waiting] = LoadIcon("state-waiting.ico");
        _icons[ModeState.ForcedOff] = LoadIcon("state-off.ico");
        _status = new Forms.ToolStripMenuItem("Статус: Принудительно выключен") { Enabled = false };
        var settings = new Forms.ToolStripMenuItem("Настройки");
        settings.Click += (_, _) => _dispatcher.BeginInvoke(show);
        _updates = new Forms.ToolStripMenuItem("Проверить обновления");
        _updates.Click += CheckUpdates;
        var quit = new Forms.ToolStripMenuItem("Выход");
        quit.Click += (_, _) => _dispatcher.BeginInvoke(exit);
        var menu = new Forms.ContextMenuStrip
        {
            BackColor = System.Drawing.Color.FromArgb(21, 29, 48),
            ForeColor = System.Drawing.Color.FromArgb(245, 246, 255),
            ShowImageMargin = false,
            Renderer = new DarkMenuRenderer()
        };
        menu.Items.AddRange([_status, new Forms.ToolStripSeparator(), settings, _updates, quit]);
        _notify = new Forms.NotifyIcon { Text = "Game Mode", Icon = _icons[ModeState.ForcedOff], ContextMenuStrip = menu, Visible = true };
        _notify.DoubleClick += (_, _) => _dispatcher.BeginInvoke(show);
        _notify.BalloonTipClicked += (_, _) => _dispatcher.BeginInvoke(show);
        _notificationTimer = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Background, (_, _) => FlushNotifications(), _dispatcher);
        _notificationTimer.Stop();
        runtime.Changed += Changed;
        runtime.Log.Added += LogAdded;
        Update(runtime.View);
    }

    private static Icon LoadIcon(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Tray", "Assets", name);
        // A build missing required artwork should fail clearly instead of showing an unrelated system icon.
        using var source = new Icon(path, SystemInformationIconSize());
        return (Icon)source.Clone();
    }

    private static System.Drawing.Size SystemInformationIconSize()
    {
        // Tray is created before the settings window is shown, so its WPF DPI is not reliable yet.
        var pixels = Math.Clamp((int)Math.Round(16 * GetDpiForSystem() / 96d), 16, 64);
        return new System.Drawing.Size(pixels, pixels);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private void Changed(RuntimeView view)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(new Action(() => Update(view)));
    }

    private void Update(RuntimeView view)
    {
        if (_disposed) return;
        if (_lastState == view.State) return;
        _lastState = view.State;
        _status.Text = "Статус: " + MainWindow.StateName(view.State);
        _notify.Text = "Game Mode · " + MainWindow.StateName(view.State);
        _notify.Icon = _icons[view.State];
    }

    private void LogAdded(LogEntry entry)
    {
        if (_disposed || entry.Level is not ("Действие" or "Ошибка" or "Внимание" or "Статус" or "ERROR" or "WARN")) return;
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed) return;
            _pending.Add(entry);
            if (DateTimeOffset.Now - _lastNotification >= TimeSpan.FromSeconds(8)) FlushNotifications();
            else _notificationTimer.Start();
        }));
    }

    private void FlushNotifications()
    {
        _notificationTimer.Stop();
        if (_disposed || _pending.Count == 0) return;
        var entry = _pending[^1];
        var text = _pending.Count == 1 ? entry.Message : $"Новых событий: {_pending.Count}. {entry.Message}\nПодробности в журнале Game Mode.";
        _notify.ShowBalloonTip(5000, "Game Mode · " + entry.Level, text.Length > 250 ? text[..247] + "…" : text,
            entry.Level is "Ошибка" or "ERROR" ? Forms.ToolTipIcon.Error : entry.Level is "Внимание" or "WARN" ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
        _pending.Clear();
        _lastNotification = DateTimeOffset.Now;
    }

    private async void CheckUpdates(object? sender, EventArgs e)
    {
        if (_disposed) return;
        _updates.Enabled = false;
        try
        {
            var message = await _runtime.CheckUpdatesAsync();
            if (_disposed) return;
            System.Windows.MessageBox.Show(message, "Game Mode — обновления", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            _runtime.Log.Write("Ошибка", "Не удалось проверить обновления: " + exception.Message);
            if (!_disposed) System.Windows.MessageBox.Show(exception.Message, "Game Mode — обновления", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { if (!_disposed) _updates.Enabled = true; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runtime.Changed -= Changed;
        _runtime.Log.Added -= LogAdded;
        _notificationTimer.Stop();
        _notify.Visible = false;
        var menu = _notify.ContextMenuStrip;
        _notify.Dispose();
        menu?.Dispose();
        foreach (var icon in _icons.Values) icon.Dispose();
    }

    private sealed class DarkMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = true; }
        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? System.Drawing.Color.FromArgb(245, 246, 255) : System.Drawing.Color.FromArgb(171, 184, 211);
            base.OnRenderItemText(e);
        }
        private sealed class DarkColors : Forms.ProfessionalColorTable
        {
            public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(21, 29, 48);
            public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(44, 60, 96);
            public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(106, 130, 207);
            public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(59, 72, 104);
            public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(59, 72, 104);
            public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(59, 72, 104);
        }
    }
}
