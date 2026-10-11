namespace GameMode.Core;

public sealed class EventLog : IGameModeLog
{
    private readonly object _gate = new();
    private readonly List<LogEntry> _entries = [];
    private readonly string _directory;
    public EventLog(string directory) { _directory = Path.Combine(directory, "logs"); Directory.CreateDirectory(_directory); }
    public event Action<LogEntry>? Added;
    public IReadOnlyList<LogEntry> Entries { get { lock (_gate) return _entries.ToArray(); } }
    public void Write(string level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);
        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > 500) _entries.RemoveAt(0);
            try
            {
                var file = Path.Combine(_directory, $"game-mode-{DateTime.Now:yyyy-MM-dd}.log");
                if (File.Exists(file) && new FileInfo(file).Length > 4 * 1024 * 1024) File.Move(file, file + ".previous", true);
                File.AppendAllText(file, $"{entry.Time:yyyy-MM-dd HH:mm:ss zzz} [{level}] {message.Replace('\r', ' ').Replace('\n', ' ')}{Environment.NewLine}");
                foreach (var old in Directory.EnumerateFiles(_directory).Where(f => File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-14))) File.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _entries.Add(new(DateTimeOffset.Now, "Ошибка", "Не удалось записать журнал на диск: " + ex.Message)); }
        }
        Added?.Invoke(entry);
    }
}
