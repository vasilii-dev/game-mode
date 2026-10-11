using System.Text.Json;

namespace GameMode.Core;

public sealed class SettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string DataDirectory { get; }
    public SettingsStore(string dataDirectory) { DataDirectory = dataDirectory; Directory.CreateDirectory(dataDirectory); }
    public string? LoadError { get; private set; }
    public AppSettings Load()
    {
        var file = Path.Combine(DataDirectory, "settings.json");
        if (!File.Exists(file)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), JsonOptions) ?? throw new InvalidDataException("Пустой файл настроек.");
            Validate(settings);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or InvalidDataException)
        {
            LoadError = $"Настройки не прочитаны: {ex.Message}. Режим выключен; исходный файл сохранён.";
            return new();
        }
    }
    public static AppSettings Clone(AppSettings settings) => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings), JsonOptions)!;
    public static void Validate(AppSettings settings)
    {
        if (settings.ThresholdMiB is < 16 or > 1048576) throw new ArgumentException("Порог: от 16 до 1048576 МиБ.");
        if (settings.PollSeconds is < 3 or > 120) throw new ArgumentException("Интервал: от 3 до 120 секунд.");
        if (settings.Exclusions is null || settings.Games is null || settings.NoRestorePaths is null) throw new ArgumentException("Списки правил повреждены.");
        foreach (var rule in settings.Exclusions.Concat(settings.Games))
        {
            if (rule.FullPath is not null && (!Path.IsPathFullyQualified(rule.FullPath) || !rule.FullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Правило должно содержать абсолютный путь к .exe.");
            if (rule.FullPath is null && (string.IsNullOrWhiteSpace(rule.ExecutableName) || Path.GetFileName(rule.ExecutableName) != rule.ExecutableName || !rule.ExecutableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Правило должно содержать имя исполняемого файла .exe.");
        }
        if (!settings.RiskAcknowledged) settings.Enabled = false;
    }
    public void Save(AppSettings settings) { Validate(settings); WriteAtomic("settings.json", settings); }
    public void SaveQueue(IReadOnlyList<RestartEntry> entries) => WriteAtomic("restart-journal.json", entries);
    public List<RestartEntry> LoadQueue()
    {
        var file = Path.Combine(DataDirectory, "restart-journal.json");
        if (!File.Exists(file)) return [];
        try { return JsonSerializer.Deserialize<List<RestartEntry>>(File.ReadAllText(file), JsonOptions) ?? []; }
        catch (Exception ex) when (ex is IOException or JsonException) { LoadError = $"Журнал возврата не прочитан: {ex.Message}. Автоматический возврат отменён."; return []; }
    }
    private void WriteAtomic<T>(string name, T value)
    {
        var file = Path.Combine(DataDirectory, name);
        var temp = file + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, value, JsonOptions); stream.Flush(true); }
        File.Move(temp, file, true);
    }
}
