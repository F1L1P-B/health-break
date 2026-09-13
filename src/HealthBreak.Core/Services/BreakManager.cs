using HealthBreak.Core.Models;

namespace HealthBreak.Core.Services;

public sealed record ReminderNotice(int Stage, string Title, string Message, BreakType SuggestedBreakType, bool IsStrict);

public sealed class BreakManager
{
    private int _deliveredStage;
    private int _repeatStage;
    private long _generation = -1;
    private DateTimeOffset? _snoozeUntil;

    public ReminderNotice? Evaluate(MonitoringSnapshot snapshot, AppSettings settings)
    {
        if (snapshot.IsPaused || snapshot.IsOnBreak || snapshot.IsIdle) return null;
        if (_generation != snapshot.SessionGeneration)
        {
            Reset();
            _generation = snapshot.SessionGeneration;
        }
        var minutes = snapshot.CurrentSessionSeconds / 60;
        var stage = minutes >= settings.StrongReminderMinutes ? 4 :
            minutes >= settings.RecommendedBreakMinutes ? 3 :
            minutes >= settings.SecondReminderMinutes ? 2 :
            minutes >= settings.FirstReminderMinutes ? 1 : 0;
        var strict = settings.StrictMode && stage == 4;
        if (stage == 0 || (!settings.NotificationsEnabled && !strict)) return null;
        if (_snoozeUntil is { } until && snapshot.Timestamp < until && !strict) return null;
        var repeated = _repeatStage > 0 && snapshot.Timestamp >= _snoozeUntil && stage >= _repeatStage;
        if (stage <= _deliveredStage && !repeated) return null;
        _deliveredStage = stage;
        _repeatStage = 0;
        _snoozeUntil = null;
        var title = strict ? "Health break required" : stage switch
        {
            1 => "A little reset?",
            2 => "Your next break is ready",
            3 => "Time for a health break",
            _ => "Give yourself a proper break"
        };
        return new(stage, title, $"You've been active for {Math.Floor(minutes):0} minutes. Take a moment to move and rest your eyes.",
            SuggestBreakType(snapshot.CurrentSessionSeconds, settings), strict);
    }

    public void Snooze(DateTimeOffset now, AppSettings settings, int minutes = 5)
    {
        _repeatStage = Math.Max(1, _deliveredStage);
        _snoozeUntil = now.AddSeconds(Math.Max(1, minutes) * 60 / settings.TimeScale);
    }

    public void Skip()
    {
        _repeatStage = 0;
        _snoozeUntil = null;
    }

    public void Reset()
    {
        _deliveredStage = 0;
        _repeatStage = 0;
        _snoozeUntil = null;
    }

    public double SecondsUntilNextReminder(MonitoringSnapshot snapshot, AppSettings settings)
    {
        if (_snoozeUntil is { } until)
            return Math.Max(0, (until - snapshot.Timestamp).TotalSeconds * settings.TimeScale);
        var thresholds = new[] { settings.FirstReminderMinutes, settings.SecondReminderMinutes,
            settings.RecommendedBreakMinutes, settings.StrongReminderMinutes };
        var next = thresholds.FirstOrDefault(t => t * 60 > snapshot.CurrentSessionSeconds);
        return next == 0 ? 0 : next * 60 - snapshot.CurrentSessionSeconds;
    }

    public static BreakType SuggestBreakType(double activeSeconds, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var activeMinutes = Math.Max(0, activeSeconds) / 60;
        if (activeMinutes >= settings.StrongReminderMinutes) return BreakType.Full;
        if (activeMinutes >= settings.SecondReminderMinutes) return BreakType.Short;
        return BreakType.Quick;
    }
}
