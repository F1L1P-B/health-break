using HealthBreak.Core.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HealthBreak.Data.Tests;

public sealed class RepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HealthBreak.Tests", Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _start = new(new DateTime(2026, 9, 12, 10, 0, 0), TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 12)));
    private static readonly Exercise[] Catalog =
    [
        new(1, "Distant focus", ExerciseCategory.Eyes, "Look into the distance.", 20, null),
        new(2, "Shoulder rolls", ExerciseCategory.Shoulders, "Roll shoulders gently.", 30, 5)
    ];

    private string DatabasePath => Path.Combine(_directory, "test.db");
    private DateOnly Today => DateOnly.FromDateTime(_start.LocalDateTime);

    [Fact]
    public void CheckpointUpsertDoesNotDoubleCountAndDemoRemainsSeparate()
    {
        using var repository = Open();
        var session = Session("normal", 600, 30);
        repository.SaveChanges([session], []);
        repository.SaveChanges([session with { ActiveSeconds = 900, LastObservedAt = _start.AddMinutes(16) }], []);
        repository.SaveChanges([Session("demo", 7200, 50) with { IsDemo = true }], []);

        var actual = repository.GetStatistics(Today, false);
        Assert.Equal(900, actual.ActiveSeconds);
        Assert.Equal(30, actual.IdleSeconds);
        Assert.Equal(1, actual.Sessions);
        Assert.Equal(900, actual.AverageSessionSeconds);
        Assert.Equal(900, actual.LongestSessionSeconds);
        Assert.Equal(7200, repository.GetStatistics(Today, true).ActiveSeconds);
    }

    [Fact]
    public void RestartClosesInterruptedSessionAtCheckpointWithoutAccruingOfflineTime()
    {
        var checkpoint = _start.AddMinutes(12);
        using (var repository = Open())
        {
            repository.SaveChanges([Session("interrupted", 700, 20) with { LastObservedAt = checkpoint }], []);
        }

        using var reopened = Open();
        var saved = Assert.Single(reopened.GetSessions(Today, false));
        Assert.Equal(checkpoint, saved.EndTime);
        Assert.Equal(checkpoint, saved.LastObservedAt);
        Assert.Equal(700, saved.ActiveSeconds);
        Assert.Equal(20, saved.IdleSeconds);
    }

    [Fact]
    public void SevenDaysIncludesZerosAndAttributesSplitSessionsToLocalDates()
    {
        using var repository = Open();
        var yesterday = _start.AddDays(-1);
        repository.SaveChanges(
            [Session("today", 45, 10), new SessionRecord("yesterday", yesterday, yesterday.AddMinutes(1), 55, 5, false)], []);

        var week = repository.GetWeek(Today, false);
        Assert.Equal(7, week.Count);
        Assert.Equal(Today.AddDays(-6), week[0].Date);
        Assert.Equal(Today, week[^1].Date);
        Assert.All(week.Take(5), day => Assert.Equal(0, day.ActiveSeconds));
        Assert.Equal(55, week[^2].ActiveSeconds);
        Assert.Equal(45, week[^1].ActiveSeconds);
    }

    [Fact]
    public void ManualExerciseCompletionIsIdempotentAndCannotBeUndoneByStaleShownEvent()
    {
        using var repository = Open();
        repository.SaveChanges([], [Break("break", true)]);
        var shown = new ExerciseHistoryRecord("break", 1, _start, false, false);
        repository.SaveExerciseHistory(shown);
        Assert.Equal(0, repository.GetStatistics(Today, false).ExercisesCompleted);

        var completed = shown with { Completed = true, Timestamp = _start.AddSeconds(20) };
        repository.SaveExerciseHistory(completed);
        repository.SaveExerciseHistory(completed);
        repository.SaveExerciseHistory(shown);

        Assert.Equal(1, repository.GetStatistics(Today, false).ExercisesCompleted);
        var saved = Assert.Single(repository.GetExerciseHistory(false));
        Assert.True(saved.Completed);
        Assert.Equal(completed.Timestamp, saved.Timestamp);
    }

    [Fact]
    public void ForeignKeysRejectExerciseHistoryForUnknownBreakOrExercise()
    {
        using var repository = Open();
        Assert.Throws<SqliteException>(() => repository.SaveExerciseHistory(new("missing", 1, _start, true, false)));
        repository.SaveChanges([], [Break("exists", true)]);
        Assert.Throws<SqliteException>(() => repository.SaveExerciseHistory(new("exists", 999, _start, true, false)));
    }

    [Fact]
    public void ReminderActionsCountOnceAndEmergencyIsAlsoASkip()
    {
        using var repository = Open();
        var snooze = new ReminderActionRecord("snooze", _start, "snooze", false);
        repository.SaveReminderAction(snooze);
        repository.SaveReminderAction(snooze);
        repository.SaveReminderAction(new("skip", _start, "skip", false));
        repository.SaveReminderAction(new("emergency", _start, "emergency", false));
        repository.SaveReminderAction(new("demo", _start, "skip", true));
        repository.SaveChanges([], [Break("cancelled", false) with { Skipped = true, Emergency = true }]);

        var actual = repository.GetStatistics(Today, false);
        Assert.Equal(1, actual.Snoozes);
        Assert.Equal(2, actual.BreaksSkipped);
        Assert.Equal(1, actual.EmergencySkips);
        Assert.Equal(0, actual.Breaks);
        Assert.Throws<ArgumentException>(() => repository.SaveReminderAction(new("bad", _start, "unexpected", false)));
    }

    [Fact]
    public void SettingsRoundTripValidatedAndDemoNeverRestoresOnRestart()
    {
        using var repository = Open();
        repository.SaveSettings(new AppSettings { IdleThresholdSeconds = 90, TrackApplications = true, DemoMode = true });
        var settings = repository.LoadSettings();
        Assert.Equal(90, settings.IdleThresholdSeconds);
        Assert.True(settings.TrackApplications);
        Assert.False(settings.DemoMode);
        Assert.Throws<ArgumentException>(() => repository.SaveSettings(new AppSettings { IdleThresholdSeconds = -1 }));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"IdleThresholdSeconds\":-9}")]
    public void InvalidStoredSettingsFallBackToValidDefaults(string json)
    {
        using var repository = Open();
        using var connection = Connection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO settings(id,json) VALUES(1,$json);";
        command.Parameters.AddWithValue("$json", json);
        command.ExecuteNonQuery();
        var settings = repository.LoadSettings();
        Assert.Equal(180, settings.IdleThresholdSeconds);
        Assert.Empty(settings.Validate());
    }

    [Fact]
    public void ApplicationUsageAggregatesCategoriesAndRejectsArbitraryContent()
    {
        using var repository = Open();
        repository.AddApplicationTime(Today, "Development", 20, false);
        repository.AddApplicationTime(Today, "Development", 30, false);
        repository.AddApplicationTime(Today, "Development", 3000, true);
        repository.AddApplicationTime(Today, "Private window title'; DROP TABLE sessions;--", 5, false);
        var usage = repository.GetApplicationUsage(Today, false);
        Assert.Equal(50, usage["Development"]);
        Assert.Equal(5, usage["Other"]);
        Assert.Equal(2, usage.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => repository.AddApplicationTime(Today, "Other", double.NaN, false));
    }

    [Fact]
    public void FailedBatchRollsBackBothSessionAndBreakChanges()
    {
        using var repository = Open();
        Assert.Throws<ArgumentOutOfRangeException>(() => repository.SaveChanges(
            [Session("valid-first", 50, 0)], [Break("invalid-second", true) with { DurationSeconds = -1 }]));
        Assert.Empty(repository.GetSessions(Today, false));
        Assert.Equal(0, repository.GetStatistics(Today, false).ActiveSeconds);
    }

    [Fact]
    public void CorruptDatabaseIsQuarantinedAndReplacedWithAUsableDatabase()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(DatabasePath, "this is not a sqlite database");

        using var repository = Open();

        Assert.NotNull(repository.RecoveryNotice);
        Assert.Equal(0, repository.GetStatistics(Today, false).ActiveSeconds);
        Assert.True(File.Exists(DatabasePath));
        Assert.True(File.Exists(DatabasePath + ".backup"));
        Assert.NotEmpty(Directory.GetFiles(_directory, "test.db.corrupt-*"));
    }

    [Fact]
    public void CorruptDatabaseRestoresTheLastVerifiedBackup()
    {
        using (var repository = Open())
        {
            repository.SaveChanges([Session("protected", 600, 30)], []);
        }
        Assert.True(File.Exists(DatabasePath + ".backup"));
        File.WriteAllText(DatabasePath, "damaged after the last clean shutdown");

        using var recovered = Open();

        Assert.Contains("restored from its safety backup", recovered.RecoveryNotice);
        Assert.Equal(600, recovered.GetStatistics(Today, false).ActiveSeconds);
        Assert.Single(recovered.GetSessions(Today, false));
        Assert.NotEmpty(Directory.GetFiles(_directory, "test.db.corrupt-*"));
    }

    [Fact]
    public void RecentBreaksAreCompletedNewestFirstWithDemoExcluded()
    {
        using var repository = Open();
        repository.SaveChanges([], [Break("older", true), Break("newer", true) with { StartTime = _start.AddHours(1) },
            Break("incomplete", false), Break("demo", true) with { IsDemo = true }]);
        var recent = repository.GetRecentBreaks(false);
        Assert.Equal(["newer", "older"], recent.Select(item => item.Id).ToArray());
        Assert.Single(repository.GetRecentBreaks(false, 1));
        Assert.Equal(2, repository.GetStatistics(Today, false).Breaks);
    }

    [Fact]
    public void SchemaIsVersionedAndWalEnabledAndDisposeReleasesFile()
    {
        using (var repository = Open())
        {
            using var connection = Connection();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(1L, command.ExecuteScalar());
            command.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("wal", command.ExecuteScalar());
        }

        using var exclusive = new FileStream(DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.Length > 0);
    }

    [Fact]
    public void NewerDatabaseVersionIsNeverSilentlyDowngraded()
    {
        using (var repository = Open())
        {
            using var connection = Connection();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=99;";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidOperationException>(() => new HealthRepository(DatabasePath));
    }

    private HealthRepository Open()
    {
        var repository = new HealthRepository(DatabasePath);
        repository.Initialize(Catalog);
        return repository;
    }

    private SqliteConnection Connection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private SessionRecord Session(string id, double active, double idle) => new(id, _start, null, active, idle, false, _start.AddSeconds(active + idle));
    private BreakRecord Break(string id, bool completed) => new(id, _start, 180, BreakType.Short, completed, false, false, false);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
