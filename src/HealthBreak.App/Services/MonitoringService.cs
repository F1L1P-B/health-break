using System.Threading.Channels;
using HealthBreak.App.Services.Native;
using HealthBreak.Core.Models;
using HealthBreak.Core.Services;
using HealthBreak.Data;

namespace HealthBreak.App.Services;

public sealed record BreakOffer(BreakType Type, IReadOnlyList<Exercise> Exercises, string Title,
    string Message, bool IsStrict, bool CountsAsReminder = false);
public sealed record AppState(MonitoringSnapshot Monitor, DailyStatistics Today,
    IReadOnlyList<DailyStatistics> Week, IReadOnlyDictionary<string, double> Applications,
    AppSettings Settings, double NextReminderSeconds, IReadOnlyList<Exercise> BreakExercises,
    IReadOnlySet<int> CompletedExerciseIds, Exercise SuggestedExercise);

/// <summary>One background owner serializes native samples, domain commands and SQLite writes.</summary>
public sealed class MonitoringService : IAsyncDisposable
{
    private sealed record Command(Action Execute, Action<Exception> Fail);

    private readonly Channel<Command> _commands = Channel.CreateUnbounded<Command>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly object _lifecycleGate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SessionManager _sessions = new();
    private readonly BreakManager _reminders = new();
    private readonly Func<bool, ActivitySample> _sample;
    private readonly Func<bool> _readStartup;
    private readonly Action<bool> _writeStartup;
    private readonly Dictionary<string, SessionRecord> _pendingSessions = [];
    private readonly Dictionary<string, BreakRecord> _pendingBreaks = [];
    private readonly Dictionary<(string BreakId, int ExerciseId, bool IsDemo), ExerciseHistoryRecord> _pendingExercises = [];
    private readonly Dictionary<string, ReminderActionRecord> _pendingActions = [];
    private readonly Dictionary<(DateOnly Date, string Category, bool IsDemo), double> _pendingApplications = [];
    private readonly string _databasePath;
    private readonly IReadOnlyList<Exercise> _catalog = ExerciseCatalog.Load();
    private readonly ExerciseSelector _selector = new();
    private readonly HashSet<int> _completed = [];
    private IReadOnlyList<Exercise> _breakExercises = [];
    private HealthRepository? _repository;
    private AppSettings _settings = new();
    private Task? _worker;
    private Task? _ready;
    private DateTimeOffset _lastTick;
    private DateTimeOffset _lastError;
    private string? _lastStorageWarning;
    private bool _disposed;

    public event Action<AppState>? StateChanged;
    public event Action<BreakOffer>? ReminderRequested;
    public event Action<string>? Error;
    public IReadOnlyList<Exercise> Catalog => _catalog;

    public MonitoringService(string databasePath, Func<bool, ActivitySample>? sample = null,
        Func<bool>? readStartup = null, Action<bool>? writeStartup = null)
    {
        _databasePath = databasePath;
        _sample = sample ?? new WindowsActivityMonitor().Sample;
        var startup = new StartupService();
        _readStartup = readStartup ?? (() => startup.IsEnabled);
        _writeStartup = writeStartup ?? startup.SetEnabled;
    }

    public Task StartAsync()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return Task.FromException(new ObjectDisposedException(nameof(MonitoringService)));
            if (_ready is not null) return _ready;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ready = ready.Task;
        _worker = Task.Run(async () =>
        {
            Exception? failure = null;
            try
            {
                _repository = new HealthRepository(_databasePath);
                _repository.Initialize(_catalog);
                if (_repository.RecoveryNotice is { } recoveryNotice) ReportError(recoveryNotice);
                ReportStorageWarning();
                _settings = _repository.LoadSettings();
                _settings.DemoMode = false;
                _lastTick = DateTimeOffset.Now;
                Tick();
                ready.TrySetResult();
                while (!_stop.IsCancellationRequested)
                {
                    while (!_stop.IsCancellationRequested && _commands.Reader.TryRead(out var command)) command.Execute();
                    if (_stop.IsCancellationRequested) break;
                    Tick();
                    await Task.Delay(500, _stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                ready.TrySetCanceled();
            }
            catch (Exception ex)
            {
                failure = ex;
                ready.TrySetException(ex);
                ReportError("Monitoring stopped: " + ex.Message);
            }
            finally
            {
                _commands.Writer.TryComplete();
                var stopped = failure ?? new ObjectDisposedException(nameof(MonitoringService));
                while (_commands.Reader.TryRead(out var command)) command.Fail(stopped);
                if (_repository is not null)
                {
                    try
                    {
                        _sessions.SetPaused(true, DateTimeOffset.Now);
                        _sessions.ResetContext(DateTimeOffset.Now);
                        Flush();
                    }
                    catch (Exception ex) { ReportError("Could not save the final checkpoint: " + ex.Message); }
                    _repository.Dispose();
                    _repository = null;
                }
            }
        });
        return ready.Task;
        }
    }

    private void Tick()
    {
        try
        {
            var sample = _sample(_settings.TrackApplications);
            var before = _sessions.Current;
            var snapshot = _sessions.Tick(sample, _settings);
            var elapsed = (sample.Timestamp - _lastTick).TotalSeconds;
            _lastTick = sample.Timestamp;
            // Category totals deliberately use only samples with fresh input, never an idle grace period.
            if (_settings.TrackApplications && sample.Available && sample.IdleSeconds < 1 &&
                !snapshot.IsPaused && !snapshot.IsOnBreak && !before.IsOnBreak)
            {
                if (elapsed is > 0 and < 2 && sample.Category is { } category)
                {
                    var key = (DateOnly.FromDateTime(sample.Timestamp.Date), category, _settings.DemoMode);
                    _pendingApplications[key] = _pendingApplications.GetValueOrDefault(key) + elapsed * _settings.TimeScale;
                }
            }
            Flush();
            Publish();
            var exercises = Select(BreakManager.SuggestBreakType(snapshot.CurrentSessionSeconds, _settings));
            var reminder = _reminders.Evaluate(snapshot, _settings);
            if (reminder is not null)
            {
                ReminderRequested?.Invoke(new(reminder.SuggestedBreakType, exercises,
                    reminder.Title, reminder.Message, reminder.IsStrict, true));
            }
        }
        catch (Exception ex)
        {
            if ((DateTimeOffset.Now - _lastError).TotalSeconds > 10)
            {
                _lastError = DateTimeOffset.Now;
                ReportError("A local monitoring or storage error occurred: " + ex.Message);
            }
        }
    }

    private void Flush()
    {
        var changes = _sessions.DrainChanges();
        foreach (var session in changes.Sessions) _pendingSessions[session.Id] = session;
        foreach (var healthBreak in changes.Breaks) _pendingBreaks[healthBreak.Id] = healthBreak;
        if (_pendingSessions.Count > 0 || _pendingBreaks.Count > 0)
        {
            _repository!.SaveChanges(_pendingSessions.Values, _pendingBreaks.Values);
            _pendingSessions.Clear();
            _pendingBreaks.Clear();
        }
        // Remove only acknowledged writes; a failed SQLite operation remains available for the next tick.
        foreach (var (key, record) in _pendingExercises.ToArray())
        {
            _repository!.SaveExerciseHistory(record);
            _pendingExercises.Remove(key);
        }
        foreach (var (id, record) in _pendingActions.ToArray())
        {
            _repository!.SaveReminderAction(record);
            _pendingActions.Remove(id);
        }
        foreach (var (key, seconds) in _pendingApplications.ToArray())
        {
            _repository!.AddApplicationTime(key.Date, key.Category, seconds, key.IsDemo);
            _pendingApplications.Remove(key);
        }
        _repository!.MaintainBackup();
        ReportStorageWarning();
    }

    private void ReportStorageWarning()
    {
        var warning = _repository?.MaintenanceWarning;
        if (warning is null)
        {
            _lastStorageWarning = null;
            return;
        }
        if (string.Equals(warning, _lastStorageWarning, StringComparison.Ordinal)) return;
        _lastStorageWarning = warning;
        ReportError(warning);
    }

    private IReadOnlyList<Exercise> Select(BreakType type) => _selector.Select(type,
        _sessions.Current.CurrentSessionSeconds, _repository!.GetExerciseHistory(_settings.DemoMode),
        _repository.GetRecentBreaks(_settings.DemoMode));

    private void Publish()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var next = _reminders.SecondsUntilNextReminder(_sessions.Current, _settings);
        var suggestion = Select(BreakManager.SuggestBreakType(_sessions.Current.CurrentSessionSeconds, _settings)).First();
        StateChanged?.Invoke(new(_sessions.Current, _repository!.GetStatistics(today, _settings.DemoMode),
            _repository.GetWeek(today, _settings.DemoMode), _repository.GetApplicationUsage(today, _settings.DemoMode),
            _settings.Copy(), next, _breakExercises.ToArray(),
            new HashSet<int>(_completed), suggestion));
    }

    public Task<BreakOffer> PreviewBreakAsync(BreakType? type = null, bool countsAsReminder = false) => Enqueue(() =>
    {
        var selectedType = type ?? BreakManager.SuggestBreakType(_sessions.Current.CurrentSessionSeconds, _settings);
        return new BreakOffer(selectedType, Select(selectedType), "A moment to reset.",
            "Step away, move gently and give your eyes a rest.", false, countsAsReminder);
    });

    public Task BeginBreakAsync(BreakOffer offer) => Enqueue(() =>
    {
        if (_sessions.Current.IsOnBreak) return;
        var id = _sessions.BeginBreak(offer.Type, _settings, DateTimeOffset.Now);
        _breakExercises = offer.Exercises.ToArray();
        _completed.Clear();
        foreach (var exercise in _breakExercises)
            QueueExercise(new(id, exercise.Id, DateTimeOffset.Now, false, _settings.DemoMode));
        Flush();
        Publish();
    });

    public Task CompleteExerciseAsync(int exerciseId) => Enqueue(() =>
    {
        if (!_sessions.Current.IsOnBreak || _sessions.Current.CurrentBreakId is not { } breakId ||
            !_breakExercises.Any(e => e.Id == exerciseId) || _completed.Contains(exerciseId)) return;
        QueueExercise(new(breakId, exerciseId, DateTimeOffset.Now, true, _settings.DemoMode));
        _completed.Add(exerciseId);
        Flush();
        Publish();
    });

    public Task FinishBreakAsync(bool completed, bool emergency = false,
        bool countAsSkippedReminder = true) => Enqueue(() =>
    {
        if (!_sessions.Current.IsOnBreak) return;
        var result = _sessions.FinishBreak(DateTimeOffset.Now, completed, emergency);
        if (result is { Completed: false } && countAsSkippedReminder)
            SaveAction(emergency ? "emergency" : "skip");
        if (result is { Completed: true }) _reminders.Reset();
        else _reminders.Skip();
        _breakExercises = [];
        _completed.Clear();
        Flush();
        Publish();
    });

    public Task SnoozeAsync() => Enqueue(() =>
    {
        _reminders.Snooze(DateTimeOffset.Now, _settings);
        SaveAction("snooze");
        Flush();
        Publish();
    });

    public Task SkipAsync(bool emergency = false, bool countAsSkippedReminder = true) => Enqueue(() =>
    {
        _reminders.Skip();
        if (countAsSkippedReminder) SaveAction(emergency ? "emergency" : "skip");
        Flush();
        Publish();
    });

    private void SaveAction(string action)
    {
        var record = new ReminderActionRecord(Guid.NewGuid().ToString("N"), DateTimeOffset.Now, action, _settings.DemoMode);
        _pendingActions[record.Id] = record;
    }

    private void QueueExercise(ExerciseHistoryRecord record)
    {
        var key = (record.BreakId, record.ExerciseId, record.IsDemo);
        if (!_pendingExercises.TryGetValue(key, out var previous) || !previous.Completed)
            _pendingExercises[key] = record;
    }

    public Task PauseAsync(bool pause) => Enqueue(() =>
    {
        _sessions.SetPaused(pause, DateTimeOffset.Now);
        Flush();
        Publish();
    });

    public Task SaveSettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var requested = settings.Copy();
        return Enqueue(() =>
        {
        var errors = requested.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
        var changingMode = requested.DemoMode != _settings.DemoMode;
        if (changingMode && _sessions.Current.IsOnBreak)
            throw new InvalidOperationException("Finish or skip the current break before changing Demo Mode.");

        Flush();
        var previousStartup = _readStartup();
        if (previousStartup != requested.StartWithWindows) _writeStartup(requested.StartWithWindows);
        try { _repository!.SaveSettings(requested); }
        catch (Exception saveError)
        {
            if (previousStartup != requested.StartWithWindows)
            {
                try { _writeStartup(previousStartup); }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("Settings could not be saved and Windows startup could not be restored.",
                        saveError, rollbackError);
                }
            }
            throw;
        }
        if (changingMode)
        {
            _sessions.ResetContext(DateTimeOffset.Now);
            _breakExercises = [];
            _completed.Clear();
        }
        if (changingMode || requested.FirstReminderMinutes != _settings.FirstReminderMinutes ||
            requested.SecondReminderMinutes != _settings.SecondReminderMinutes ||
            requested.RecommendedBreakMinutes != _settings.RecommendedBreakMinutes ||
            requested.StrongReminderMinutes != _settings.StrongReminderMinutes ||
            requested.StrictMode != _settings.StrictMode || requested.NotificationsEnabled != _settings.NotificationsEnabled)
            _reminders.Reset();

        // Persisted settings and runtime mode change together. Any later checkpoint failure is retried by Tick.
        _settings = requested;
        _lastTick = DateTimeOffset.Now;
        Tick();
        });
    }

    private Task Enqueue(Action action) => Enqueue(() => { action(); return true; });
    private Task<T> Enqueue<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lifecycleGate)
        {
            if (_disposed)
                result.TrySetException(new ObjectDisposedException(nameof(MonitoringService)));
            else if (_worker is null)
                result.TrySetException(new InvalidOperationException("Start monitoring before issuing commands."));
            else if (!_commands.Writer.TryWrite(new Command(() =>
            {
                try { result.TrySetResult(action()); }
                catch (Exception ex) { result.TrySetException(ex); }
            }, exception => result.TrySetException(exception))))
                result.TrySetException(new InvalidOperationException("The monitoring worker is no longer running."));
        }
        return result.Task;
    }

    public async ValueTask DisposeAsync()
    {
        Task? worker;
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                worker = _worker;
            }
            else
            {
                _disposed = true;
                _commands.Writer.TryComplete();
                _stop.Cancel();
                worker = _worker;
            }
        }
        if (worker is not null) await worker.ConfigureAwait(false);
        lock (_lifecycleGate)
        {
            _stop.Dispose();
        }
    }

    private void ReportError(string message)
    {
        // UI dispatch failures must never terminate the worker or leave queued callers awaiting forever.
        try { Error?.Invoke(message); }
        catch { }
    }
}

