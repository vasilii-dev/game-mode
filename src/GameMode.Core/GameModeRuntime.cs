using System.Net;
using System.Text.Json;

namespace GameMode.Core;

public sealed class GameModeRuntime : IAsyncDisposable
{
    private readonly SettingsStore _store;
    private readonly IProcessPlatform _platform;
    private readonly bool _preview;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _tick = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource _actions = new();
    private AppSettings _settings;
    private ProcessScan _lastScan = new([], false);
    private readonly List<RestartEntry> _queue;
    private readonly Dictionary<Guid, DateTimeOffset> _matches = [];
    private readonly HashSet<string> _reported = [];
    private Task? _loop;
    private bool _session;
    private long _closedBytes;
    private int _closedCount;
    private RuntimeView _view;
    public IGameModeLog Log { get; }
    public string DataDirectory => _store.DataDirectory;
    public AppSettings Settings { get { lock (_gate) return SettingsStore.Clone(_settings); } }
    public RuntimeView View { get { lock (_gate) return _view; } }
    public event Action<RuntimeView>? Changed;

    public GameModeRuntime(SettingsStore store, IProcessPlatform platform, IGameModeLog log, bool preview = false)
    {
        _store = store; _platform = platform; Log = log; _preview = preview;
        _settings = store.Load(); _queue = store.LoadQueue();
        foreach (var entry in _queue.Where(e => e.State is RestoreState.Pending or RestoreState.Interrupted))
        { entry.State = RestoreState.Interrupted; entry.Detail = "Предыдущий сеанс прерван. Автоматический запуск отменён; проверьте приложение вручную."; }
        _view = BuildView();
        if (store.LoadError is not null) Log.Write("Ошибка", store.LoadError);
        if (_queue.Any(e => e.State == RestoreState.Interrupted)) Log.Write("Внимание", "Есть прерванный сеанс: программы из него не будут автоматически запущены.");
        Log.Added += OnLog;
    }
    private void OnLog(LogEntry _) => Publish();
    public void Start() => _loop ??= Task.Run(RunAsync);
    private async Task RunAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try { await TickAsync(_lifetime.Token); await Task.Delay(TimeSpan.FromSeconds(Settings.PollSeconds), _lifetime.Token); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
            catch (Exception ex) { Log.Write("Ошибка", "Ошибка цикла мониторинга: " + ex.Message); await Task.Delay(1000, _lifetime.Token); }
        }
    }
    public void SetEnabled(bool enabled) => UpdateSettings(s => s.Enabled = enabled);
    public void UpdateSettings(Action<AppSettings> update)
    {
        lock (_gate)
        {
            var next = SettingsStore.Clone(_settings); update(next); SettingsStore.Validate(next);
            _store.Save(next); _settings = next;
            // Any policy change invalidates an in-flight graceful close authorization.
            _actions.Cancel(); _actions.Dispose(); _actions = new();
        }
        Publish();
    }
    public void SetRestoreEnabled(Guid id, bool enabled) => SetRestoreEnabled([id], enabled);
    public void SetRestoreEnabled(IReadOnlyCollection<Guid> ids, bool enabled)
    {
        lock (_gate)
        {
            var entries = _queue.Where(e => ids.Contains(e.Id)).ToArray(); if (entries.Length == 0) return;
            var next = SettingsStore.Clone(_settings);
            foreach (var path in entries.Where(e => e.Path != null).Select(e => e.Path!).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                next.NoRestorePaths.RemoveAll(p => ProcessPolicy.SamePath(p, path));
                if (!enabled) next.NoRestorePaths.Add(path);
            }
            _store.Save(next); _settings = next;
            foreach (var entry in entries) entry.RestoreEnabled = enabled;
            SaveQueue();
        }
        Publish();
    }
    // Refresh only reads processes, including while the settings window is open.
    public async Task RefreshAsync()
    {
        if (_preview) { Publish(); return; }
        await _tick.WaitAsync(_lifetime.Token);
        try { var scan = await _platform.ScanAsync(_lifetime.Token); lock (_gate) _lastScan = scan; Publish(); }
        finally { _tick.Release(); }
    }
    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        if (_preview) return;
        await _tick.WaitAsync(cancellationToken);
        try
        {
            var scan = await _platform.ScanAsync(cancellationToken);
            AppSettings settings; CancellationToken actionToken;
            lock (_gate)
            {
                _lastScan = scan; settings = SettingsStore.Clone(_settings); actionToken = _actions.Token;
                foreach (var rule in settings.Games.Where(r => r.Enabled)) if (scan.Processes.Any(p => ProcessPolicy.Matches(rule, p))) _matches[rule.Id] = DateTimeOffset.Now;
            }
            var games = ProcessPolicy.DetectGames(settings, scan.Processes);
            var active = settings.Enabled && settings.RiskAcknowledged && games.Count > 0;
            Publish();
            if (!scan.SafetyInventoryAvailable)
            { ReportOnce("inventory:" + scan.Error, "Очистка приостановлена: " + (scan.Error ?? "Не удалось проверить службы и антивирусы.")); return; }
            if (active)
            {
                if (!_session) { _session = true; Log.Write("Статус", "Включен: обнаружена игра. Порог " + settings.ThresholdMiB + " МиБ Working Set."); }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, actionToken, _lifetime.Token);
                foreach (var process in scan.Processes.OrderByDescending(p => p.WorkingSetBytes))
                {
                    if (linked.IsCancellationRequested) break;
                    if (!ProcessPolicy.CanClose(process, scan, Settings, Environment.ProcessId, out var reason))
                    {
                        if (process.WorkingSetBytes > (long)settings.ThresholdMiB * 1048576)
                            ReportOnce(process.Identity + ":" + reason, $"Пропущен {process.Name} (PID {process.Id}): {reason}");
                        continue;
                    }
                    RestartCapture capture;
                    try { capture = await _platform.CaptureRestartAsync(process, linked.Token); }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { capture = new(null, "Не удалось получить сведения запуска: " + ex.Message); }
                    var entry = new RestartEntry { Name = process.Name, Path = process.FullPath, Launch = capture.Launch,
                        RestoreEnabled = !Settings.NoRestorePaths.Any(p => ProcessPolicy.SamePath(p, process.FullPath)),
                        State = RestoreState.Interrupted, Detail = capture.Reason ?? "Записаны путь, аргументы, рабочая папка и сведения об окнах. Закрытие ещё не подтверждено." };
                    lock (_gate) { _queue.Add(entry); SaveQueue(); } // Durable before the first close request.
                    Log.Write("Действие", $"Закрытие {process.Name} (PID {process.Id}, {process.WorkingSetMiB:F0} МиБ). {(capture.Launch is null ? "Автовозврат недоступен: " + capture.Reason : "Сведения для возврата сохранены.")}");
                    CloseResult result;
                    try
                    {
                        result = await _platform.CloseAsync(process, Settings.AllowForceClose,
                            (fresh, target) => !linked.IsCancellationRequested && ProcessPolicy.CanClose(target, fresh, Settings, Environment.ProcessId, out _), linked.Token);
                    }
                    catch (OperationCanceledException) { result = new(false, false, "Действие прервано изменением настроек или выходом. Закрытие не подтверждено."); }
                    catch (Exception ex) { result = new(false, false, "Ошибка закрытия: " + ex.Message); }
                    lock (_gate)
                    {
                        entry.State = result.Closed ? RestoreState.Pending : RestoreState.Skipped;
                        entry.Detail = result.Detail + (capture.Launch is null ? " Возврат недоступен: " + capture.Reason : "");
                        if (result.Closed) { _closedCount++; _closedBytes += process.WorkingSetBytes; }
                        SaveQueue();
                    }
                    Log.Write(result.Closed ? "Действие" : "Внимание", process.Name + ": " + entry.Detail);
                }
            }
            // Turning off cancels cleanup immediately; reopening programs waits until the last detected game exits.
            else if (_session && games.Count == 0)
            {
                Log.Write("Статус", "Игра завершена. Проверяю возврат закрытых приложений.");
                _session = !await RestoreAsync(cancellationToken);
            }
            Publish();
        }
        finally { _tick.Release(); }
    }
    private async Task<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        RestartEntry[] pending; lock (_gate) pending = _queue.Where(e => e.State == RestoreState.Pending).ToArray();
        foreach (var entry in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scan = await _platform.ScanAsync(cancellationToken);
            var settings = Settings;
            if (ProcessPolicy.DetectGames(settings, scan.Processes).Count > 0) return false;
            if (!scan.SafetyInventoryAvailable) { Log.Write("Внимание", "Возврат отложен: не удалось проверить безопасность процессов."); return false; }
            if (!ProcessPolicy.CanRestore(entry, scan, settings, out var reason))
            { lock (_gate) { entry.State = RestoreState.Skipped; entry.Detail = reason; SaveQueue(); } Log.Write("Внимание", entry.Name + ": " + reason); continue; }
            // Mark attempted durably BEFORE creation, so a crash cannot repeat the launch.
            lock (_gate) { entry.State = RestoreState.Interrupted; entry.Detail = "Повторный запуск начат; результат ещё не подтверждён."; SaveQueue(); }
            LaunchResult result;
            try
            {
                result = await _platform.RestartAsync(entry.Launch!, fresh =>
                {
                    lock (_gate)
                    {
                        var pendingEntry = new RestartEntry { Id = entry.Id, Name = entry.Name, Path = entry.Path,
                            Launch = entry.Launch, RestoreEnabled = entry.RestoreEnabled, State = RestoreState.Pending };
                        return !_lifetime.IsCancellationRequested && ProcessPolicy.DetectGames(_settings, fresh.Processes).Count == 0 &&
                            ProcessPolicy.CanRestore(pendingEntry, fresh, _settings, out _);
                    }
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { result = new(false, "Ошибка повторного запуска: " + ex.Message); }
            lock (_gate) { entry.State = result.Started ? RestoreState.Started : RestoreState.Failed; entry.Detail = result.Detail; SaveQueue(); }
            Log.Write(result.Started ? "Действие" : "Внимание", entry.Name + ": " + result.Detail);
        }
        return true;
    }
    private void SaveQueue()
    {
        while (_queue.Count > 200 && _queue[0].State != RestoreState.Pending) _queue.RemoveAt(0);
        _store.SaveQueue(_queue);
    }
    private void ReportOnce(string key, string message)
    { lock (_gate) { if (_reported.Count > 2000) _reported.Clear(); if (!_reported.Add(key)) return; } Log.Write("Защита", message); }
    private RuntimeView BuildView()
    {
        var games = ProcessPolicy.DetectGames(_settings, _lastScan.Processes);
        var state = !_settings.Enabled || !_settings.RiskAcknowledged ? ModeState.ForcedOff : games.Count > 0 ? ModeState.Active : ModeState.Waiting;
        var explanation = state switch { ModeState.ForcedOff => "Очистка отключена пользователем. Включите режим, когда будете готовы.",
            ModeState.Waiting => _settings.ExperimentalGameDetection ? "Ожидаю игру из списка или библиотеки Steam. Экспериментальный детект включён." : "Слежу за играми из списка. Очистка начнётся при обнаружении игры.",
            _ => "Игра обнаружена. Подходящие приложения выше порога будут закрываться." };
        if (state == ModeState.Active && !_lastScan.SafetyInventoryAvailable) explanation = "Игра обнаружена, но очистка приостановлена: проверки безопасности недоступны. См. журнал.";
        else if (state == ModeState.Active && games.Any(p => p.GameLibraryHint != null && !_settings.Games.Any(r => r.Enabled && ProcessPolicy.Matches(r, p))))
            explanation = "Экспериментальный детект: " + string.Join(", ", games.Select(p => p.GameLibraryHint ?? p.Name).Distinct());
        var queue = JsonSerializer.Deserialize<List<RestartEntry>>(JsonSerializer.Serialize(_queue), SettingsStore.JsonOptions)!;
        return new(state, explanation, games, _lastScan.Processes, queue, Log.Entries, _closedBytes, _closedCount, new Dictionary<Guid, DateTimeOffset>(_matches));
    }
    private void Publish()
    { RuntimeView view; lock (_gate) { _view = view = BuildView(); } Changed?.Invoke(view); }

    public async Task<string> CheckUpdatesAsync()
    {
        const string source = "https://api.github.com/repos/vasilii-dev/game-mode/releases/latest";
        string message;
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GameMode/" + AppVersion.Numeric);
            using var response = await client.GetAsync(source, _lifetime.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) message = "Опубликованный релиз недоступен: репозиторий может быть закрытым или релизы ещё не созданы. Проверить обновление сейчас нельзя.";
            else if (!response.IsSuccessStatusCode) message = $"GitHub не разрешил проверку обновлений (HTTP {(int)response.StatusCode}). Попробуйте позже.";
            else
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
                if (!Version.TryParse(tag.TrimStart('v', 'V').Split('-', '+')[0], out var latest)) message = "Релиз найден, но его номер версии не удалось сравнить: " + tag;
                else message = latest > AppVersion.Current ? $"Доступна версия {tag}. Откройте раздел Releases репозитория vasilii-dev/game-mode. Установка выполняется вручную." : "На GitHub опубликована версия " + tag + "; более новая версия не найдена.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        { message = "Проверка обновлений не выполнена: " + ex.Message; }
        Log.Write("Обновления", message); return message;
    }
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel(); lock (_gate) _actions.Cancel();
        if (_loop is not null) try { await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        // Explicit exit must not silently relaunch anything. Pending records remain visible on the next start.
        lock (_gate) SaveQueue();
        Log.Added -= OnLog; _actions.Dispose(); _lifetime.Dispose(); _tick.Dispose();
    }
}
