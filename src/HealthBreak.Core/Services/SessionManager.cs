using HealthBreak.Core.Models;

namespace HealthBreak.Core.Services;

/// <summary>Pure, single-owner state machine. All time comes from supplied samples.</summary>
public sealed class SessionManager
{
    private readonly List<SessionRecord> _sessions = [];
    private readonly Dictionary<string, SessionRecord> _changedSessions = [];
    private readonly Dictionary<string, BreakRecord> _changedBreaks = [];
    private readonly List<BreakRecord> _breaks = [];
    private readonly Dictionary<string, double> _provisional = [];
    private SessionRecord? _session;
    private DateTimeOffset? _lastAt;
    private DateTimeOffset _lastInput;
    private DateTimeOffset _episodeStart;
    private bool _episodeIdle;
    private bool _paused;
    private double _continuous;
    private double _sinceBreak;
    private long _generation;
    private AppSettings _settings = new();
    private BreakRecord? _explicitBreak;
    private double _breakTarget;

    public MonitoringSnapshot Current { get; private set; } = new(DateTimeOffset.Now, 0, 0, 0, 0, 0,
        false, false, null, 0, null, null, false);

    public MonitoringSnapshot Tick(ActivitySample sample, AppSettings settings)
    {
        _settings = settings.Copy();
        var now = sample.Timestamp;
        var reportedInput = now.AddSeconds(-Math.Clamp(double.IsFinite(sample.IdleSeconds) ? sample.IdleSeconds : 0, 0, 31536000));
        if (_lastAt is null)
        {
            _lastAt = now;
            _lastInput = reportedInput;
            _episodeStart = now;
            _episodeIdle = !sample.Available || sample.IdleSeconds * settings.TimeScale >= settings.IdleThresholdSeconds;
            return Snapshot(sample);
        }

        var previous = _lastAt.Value;
        if (now <= previous) return Current; // A backwards clock adjustment must never create time.
        var elapsed = (now - previous).TotalSeconds;
        _lastAt = now;
        if (_explicitBreak is not null)
        {
            AddInterval(previous, now, false);
            _explicitBreak = _explicitBreak with { DurationSeconds = Math.Max(0,
                (now - _explicitBreak.StartTime).TotalSeconds * settings.TimeScale) };
            _changedBreaks[_explicitBreak.Id] = _explicitBreak;
            return Snapshot(sample);
        }
        if (_paused)
        {
            ResetEpisode(now, reportedInput);
            return Snapshot(sample);
        }

        // Missed polling intervals may be sleep, lock or a stalled host: never credit them as work.
        var gap = elapsed > 10;
        if (gap || !sample.Available)
        {
            ReclassifyProvisional();
            _episodeIdle = true;
            AddInterval(previous, now, false);
            if (sample.Available && reportedInput > _lastInput.AddMilliseconds(50))
            {
                FinalizeEpisode(now);
                ResetEpisode(now, reportedInput);
            }
            return Snapshot(sample);
        }

        if (reportedInput > _lastInput.AddMilliseconds(50))
        {
            var resumedAt = reportedInput < previous ? previous : reportedInput > now ? now : reportedInput;
            AddEpisodeInterval(previous, resumedAt);
            FinalizeEpisode(resumedAt);
            ResetEpisode(resumedAt, reportedInput);
            AddEpisodeInterval(resumedAt, now);
        }
        else
        {
            AddEpisodeInterval(previous, now);
        }
        return Snapshot(sample);
    }

    public string BeginBreak(BreakType type, AppSettings settings, DateTimeOffset now)
    {
        if (_explicitBreak is not null) return _explicitBreak.Id;
        _settings = settings.Copy();
        // Do not lose the fraction of an interval between the last sample and the button click.
        if (_lastAt is { } last && now > last && !_paused) AddEpisodeInterval(last, now);
        CloseSession(now);
        _provisional.Clear();
        _explicitBreak = new(Guid.NewGuid().ToString("N"), now, 0, type, false, false, false, settings.DemoMode);
        _changedBreaks[_explicitBreak.Id] = _explicitBreak;
        _breakTarget = BreakDuration(type);
        _lastAt = now;
        Current = Current with { Timestamp = now, IsOnBreak = true, CurrentBreakType = type,
            CurrentBreakId = _explicitBreak.Id, BreakRemainingSeconds = _breakTarget };
        return _explicitBreak.Id;
    }

    public BreakRecord? FinishBreak(DateTimeOffset now, bool completed, bool emergency = false)
    {
        if (_explicitBreak is null) return null;
        if (_lastAt is { } last && now > last) AddInterval(last, now, false);
        var duration = Math.Max(0, (now - _explicitBreak.StartTime).TotalSeconds * _settings.TimeScale);
        var successful = completed && duration >= _breakTarget - 0.1;
        var result = _explicitBreak with { DurationSeconds = duration, Completed = successful,
            Skipped = !successful, Emergency = emergency };
        _explicitBreak = null;
        _breaks.Add(result);
        _changedBreaks[result.Id] = result;
        CloseSession(now);
        if (successful)
        {
            // A completed guided break is an explicit reset chosen by the user.
            // Natural short idle breaks can still use the configurable credit rules.
            _continuous = 0;
            _generation++;
            _sinceBreak = 0;
        }
        _lastAt = now;
        ResetEpisode(now, now);
        Current = Current with { Timestamp = now, CurrentSessionSeconds = _continuous,
            TimeSinceLastBreakSeconds = _sinceBreak, IsOnBreak = false, CurrentBreakType = null,
            CurrentBreakId = null, BreakRemainingSeconds = 0, SessionGeneration = _generation,
            BreaksToday = _breaks.Count(b => b.Completed && LocalDate(b.StartTime) == LocalDate(now)) };
        return result;
    }

    public void SetPaused(bool value, DateTimeOffset now)
    {
        if (_paused == value) return;
        if (value && _lastAt is { } last && now > last && _explicitBreak is null) AddEpisodeInterval(last, now);
        CloseSession(now);
        _paused = value;
        _lastAt = now;
        ResetEpisode(now, now);
        Current = Current with { Timestamp = now, IsPaused = value };
    }

    public void ResetContext(DateTimeOffset now)
    {
        if (_explicitBreak is not null) FinishBreak(now, false);
        CloseSession(now);
        _sessions.Clear();
        _breaks.Clear();
        _provisional.Clear();
        _session = null;
        _lastAt = null;
        _continuous = 0;
        _sinceBreak = 0;
        _generation++;
        Current = new(now, 0, 0, 0, 0, 0, _paused, false, null, 0, null, null, _settings.DemoMode,
            SessionGeneration: _generation);
    }

    public SessionChanges DrainChanges()
    {
        var changes = new SessionChanges(_changedSessions.Values.ToArray(), _changedBreaks.Values.ToArray());
        _changedSessions.Clear();
        _changedBreaks.Clear();
        return changes;
    }

    public static double BreakDuration(BreakType type) => type switch
    {
        BreakType.Quick => 45,
        BreakType.Short => 150,
        _ => 300
    };

    private void AddEpisodeInterval(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start) return;
        var quietSeconds = (end - _lastInput).TotalSeconds * _settings.TimeScale;
        if (!_episodeIdle && quietSeconds >= _settings.IdleThresholdSeconds)
        {
            ReclassifyProvisional();
            _episodeIdle = true;
        }
        AddInterval(start, end, !_episodeIdle, provisional: !_episodeIdle);
    }

    private void FinalizeEpisode(DateTimeOffset end)
    {
        var duration = Math.Max(0, (end - _episodeStart).TotalSeconds * _settings.TimeScale);
        if (duration < _settings.MinimumBreakSeconds) return;
        ReclassifyProvisional();
        var type = duration > _settings.FullBreakSeconds ? BreakType.Full : BreakType.Short;
        var record = new BreakRecord(Guid.NewGuid().ToString("N"), _episodeStart, duration, type,
            true, false, false, _settings.DemoMode);
        _breaks.Add(record);
        _changedBreaks[record.Id] = record;
        CloseSession(end);
        ApplyBreakCredit(type);
        _sinceBreak = 0;
    }

    private void ApplyBreakCredit(BreakType type)
    {
        if (type == BreakType.Full || (type == BreakType.Short && _settings.ShortBreakResetsSession))
            _continuous = 0;
        else
            _continuous = Math.Max(0, _continuous - (type == BreakType.Quick ? 5 : _settings.ShortBreakCreditMinutes) * 60);
        _generation++;
    }

    private void ResetEpisode(DateTimeOffset start, DateTimeOffset reportedInput)
    {
        _episodeStart = start;
        _lastInput = reportedInput;
        _episodeIdle = false;
        _provisional.Clear();
    }

    private void AddInterval(DateTimeOffset start, DateTimeOffset end, bool active, bool provisional = false)
    {
        while (start < end)
        {
            var midnight = new DateTimeOffset(start.Date.AddDays(1), start.Offset);
            var until = end < midnight ? end : midnight;
            if (_session is null || LocalDate(_session.StartTime) != LocalDate(start))
            {
                CloseSession(start);
                _session = new(Guid.NewGuid().ToString("N"), start, null, 0, 0, _settings.DemoMode, start);
                _sessions.Add(_session);
            }
            var seconds = (until - start).TotalSeconds * _settings.TimeScale;
            UpdateSession(_session with
            {
                ActiveSeconds = _session.ActiveSeconds + (active ? seconds : 0),
                IdleSeconds = _session.IdleSeconds + (active ? 0 : seconds),
                LastObservedAt = until
            });
            if (active)
            {
                _continuous += seconds;
                _sinceBreak += seconds;
                if (provisional) _provisional[_session!.Id] = _provisional.GetValueOrDefault(_session.Id) + seconds;
            }
            start = until;
        }
    }

    private void ReclassifyProvisional()
    {
        foreach (var (id, amount) in _provisional)
        {
            var record = _sessions.First(s => s.Id == id);
            var correction = Math.Min(record.ActiveSeconds, amount);
            UpdateSession(record with { ActiveSeconds = record.ActiveSeconds - correction,
                IdleSeconds = record.IdleSeconds + correction });
            _continuous = Math.Max(0, _continuous - correction);
            _sinceBreak = Math.Max(0, _sinceBreak - correction);
        }
        _provisional.Clear();
    }

    private void CloseSession(DateTimeOffset now)
    {
        if (_session is not null)
        {
            UpdateSession(_session with { EndTime = now, LastObservedAt = now });
            _session = null;
        }
    }

    private void UpdateSession(SessionRecord value)
    {
        var index = _sessions.FindIndex(s => s.Id == value.Id);
        if (index >= 0) _sessions[index] = value;
        if (_session?.Id == value.Id) _session = value;
        _changedSessions[value.Id] = value;
    }

    private MonitoringSnapshot Snapshot(ActivitySample sample)
    {
        var today = LocalDate(sample.Timestamp);
        var daily = _sessions.Where(s => LocalDate(s.StartTime) == today).ToArray();
        var remaining = _explicitBreak is null ? 0 : Math.Max(0, _breakTarget -
            (sample.Timestamp - _explicitBreak.StartTime).TotalSeconds * _settings.TimeScale);
        Current = new(sample.Timestamp, daily.Sum(s => s.ActiveSeconds), daily.Sum(s => s.IdleSeconds),
            _continuous, _sinceBreak, _breaks.Count(b => b.Completed && LocalDate(b.StartTime) == today),
            _paused, _explicitBreak is not null, _explicitBreak?.Type, remaining,
            _settings.TrackApplications ? sample.ProcessName : null,
            _settings.TrackApplications ? sample.Category : null, _settings.DemoMode,
            _explicitBreak?.Id, _episodeIdle || !sample.Available, _generation);
        return Current;
    }

    private static DateOnly LocalDate(DateTimeOffset value) => DateOnly.FromDateTime(value.Date);
}
