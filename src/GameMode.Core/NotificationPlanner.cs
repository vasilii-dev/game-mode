namespace GameMode.Core;

// One summary after a game session; errors share a ten-minute cooldown. Full detail stays in the log.
public sealed class NotificationPlanner
{
    private bool _session;
    private int _initialClosed;
    private DateTimeOffset? _lastToast;
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    public string? Observe(RuntimeView view, bool enabled, DateTimeOffset now)
    {
        if (!_session && view.State == ModeState.Active)
        { _session = true; _initialClosed = view.ClosedCount; }
        if (!_session || view.Games.Count > 0) return null;
        _session = false;
        var closed = view.ClosedCount - _initialClosed;
        if (!enabled || closed <= 0 || !CanNotify(now)) return null;
        _lastToast = now;
        return $"Игра завершена. Game Mode закрыл процессов: {closed}. Результаты закрытия и возврата — в журнале.";
    }

    public string? Error(LogEntry entry, bool enabled, DateTimeOffset now)
    {
        if (!enabled || entry.Level is not ("Ошибка" or "ERROR") || !CanNotify(now)) return null;
        _lastToast = now;
        return "Не удалось выполнить действие. " + entry.Message + " Подробности в журнале Game Mode.";
    }

    private bool CanNotify(DateTimeOffset now) => _lastToast == null || now - _lastToast.Value >= Cooldown;
}
