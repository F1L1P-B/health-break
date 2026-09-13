namespace HealthBreak.Core.Models;

public enum BreakType { Quick, Short, Full }
public enum ExerciseCategory { Eyes, Neck, Shoulders, Back, Movement }

public sealed record ActivitySample(DateTimeOffset Timestamp, double IdleSeconds, bool Available,
    string? ProcessName = null, string? Category = null);

public sealed record SessionRecord(string Id, DateTimeOffset StartTime, DateTimeOffset? EndTime,
    double ActiveSeconds, double IdleSeconds, bool IsDemo, DateTimeOffset? LastObservedAt = null);

public sealed record BreakRecord(string Id, DateTimeOffset StartTime, double DurationSeconds,
    BreakType Type, bool Completed, bool Skipped, bool Emergency, bool IsDemo);

public sealed record Exercise(int Id, string Name, ExerciseCategory Category, string Description,
    int DurationSeconds, int? Repetitions);

public sealed record ExerciseHistoryRecord(string BreakId, int ExerciseId, DateTimeOffset Timestamp,
    bool Completed, bool IsDemo);

public sealed record ReminderActionRecord(string Id, DateTimeOffset Timestamp, string Action, bool IsDemo);

public sealed record DailyStatistics(DateOnly Date, double ActiveSeconds, double IdleSeconds,
    int Sessions, int Breaks, double AverageSessionSeconds, double LongestSessionSeconds,
    int ExercisesCompleted, int BreaksSkipped, int Snoozes, int EmergencySkips);

public sealed record SessionChanges(IReadOnlyList<SessionRecord> Sessions, IReadOnlyList<BreakRecord> Breaks);

public sealed record MonitoringSnapshot(DateTimeOffset Timestamp, double ActiveSecondsToday,
    double IdleSecondsToday, double CurrentSessionSeconds, double TimeSinceLastBreakSeconds,
    int BreaksToday, bool IsPaused, bool IsOnBreak, BreakType? CurrentBreakType,
    double BreakRemainingSeconds, string? CurrentProcess, string? CurrentCategory, bool IsDemo,
    string? CurrentBreakId = null, bool IsIdle = false, long SessionGeneration = 0);
