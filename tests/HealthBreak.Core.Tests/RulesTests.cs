using System.Text.Json;
using HealthBreak.Core.Models;
using HealthBreak.Core.Services;
using Xunit;

namespace HealthBreak.Core.Tests;

public sealed class RulesTests
{
    private static MonitoringSnapshot Snapshot(double minutes, DateTimeOffset? at = null, long generation = 0) =>
        new(at ?? DateTimeOffset.Now, minutes * 60, 0, minutes * 60, minutes * 60, 0, false, false,
            null, 0, null, null, false, SessionGeneration: generation);

    [Fact]
    public void ReminderStagesFireOnceUntilNextStage()
    {
        var manager = new BreakManager();
        var settings = new AppSettings();
        Assert.Null(manager.Evaluate(Snapshot(29), settings));
        Assert.Equal(1, manager.Evaluate(Snapshot(30), settings)?.Stage);
        manager.Skip();
        Assert.Null(manager.Evaluate(Snapshot(40), settings));
        Assert.Equal(2, manager.Evaluate(Snapshot(45), settings)?.Stage);
        Assert.Null(manager.Evaluate(Snapshot(50), settings));
        Assert.Equal(3, manager.Evaluate(Snapshot(60), settings)?.Stage);
        Assert.Equal(4, manager.Evaluate(Snapshot(75), settings)?.Stage);
        Assert.Null(manager.Evaluate(Snapshot(100), settings));
        Assert.NotNull(manager.Evaluate(Snapshot(45, generation: 1), settings));
    }

    [Fact]
    public void SuggestedBreakLengthFollowsConfiguredReminderStages()
    {
        var settings = new AppSettings();
        Assert.Equal(BreakType.Quick, BreakManager.SuggestBreakType(30 * 60, settings));
        Assert.Equal(BreakType.Short, BreakManager.SuggestBreakType(45 * 60, settings));
        Assert.Equal(BreakType.Short, BreakManager.SuggestBreakType(60 * 60, settings));
        Assert.Equal(BreakType.Full, BreakManager.SuggestBreakType(75 * 60, settings));

        var custom = new AppSettings
        {
            FirstReminderMinutes = 10,
            SecondReminderMinutes = 20,
            RecommendedBreakMinutes = 30,
            StrongReminderMinutes = 40
        };
        Assert.Equal(BreakType.Quick, BreakManager.SuggestBreakType(19 * 60, custom));
        Assert.Equal(BreakType.Short, BreakManager.SuggestBreakType(20 * 60, custom));
        Assert.Equal(BreakType.Full, BreakManager.SuggestBreakType(40 * 60, custom));
    }

    [Fact]
    public void CompletedBreakGenerationDoesNotImmediatelyRepeatReminder()
    {
        var manager = new BreakManager();
        var settings = new AppSettings();
        Assert.Equal(2, manager.Evaluate(Snapshot(45), settings)?.Stage);
        Assert.Null(manager.Evaluate(Snapshot(0, generation: 1), settings));
        Assert.Equal(30 * 60, manager.SecondsUntilNextReminder(Snapshot(0, generation: 1), settings));
    }

    [Fact]
    public void SnoozeRepeatsAfterFiveLogicalMinutes()
    {
        var manager = new BreakManager();
        var settings = new AppSettings { DemoMode = true };
        var now = DateTimeOffset.Now;
        Assert.NotNull(manager.Evaluate(Snapshot(30, now), settings));
        manager.Snooze(now, settings);
        Assert.Null(manager.Evaluate(Snapshot(34, now.AddSeconds(4)), settings));
        Assert.NotNull(manager.Evaluate(Snapshot(35, now.AddSeconds(5)), settings));
    }

    [Fact]
    public void StrictRequirementOverridesDisabledNotificationsAndSnooze()
    {
        var manager = new BreakManager();
        var settings = new AppSettings { StrictMode = true, NotificationsEnabled = false };
        Assert.Null(manager.Evaluate(Snapshot(60), settings));
        manager.Snooze(DateTimeOffset.Now, settings);
        Assert.True(manager.Evaluate(Snapshot(75), settings)?.IsStrict);
    }

    [Fact]
    public void IdleAndPausedMonitoringDoNotRequestBreaks()
    {
        var manager = new BreakManager();
        Assert.Null(manager.Evaluate(Snapshot(90) with { IsPaused = true }, new()));
        Assert.Null(manager.Evaluate(Snapshot(90) with { IsIdle = true }, new()));
        Assert.Null(manager.Evaluate(Snapshot(90) with { IsOnBreak = true }, new()));
    }

    [Fact]
    public void CatalogAndSelectionProvideCoverageAndAvoidRecentExercises()
    {
        var catalog = ExerciseCatalog.Load();
        Assert.True(catalog.Count >= 24);
        Assert.Equal(catalog.Count, catalog.Select(e => e.Id).Distinct().Count());
        Assert.Equal(5, catalog.Select(e => e.Category).Distinct().Count());
        var selector = new ExerciseSelector(catalog);
        var first = selector.Select(BreakType.Full, 100 * 60, []);
        Assert.Contains(first, e => e.Category == ExerciseCategory.Eyes);
        Assert.Contains(first, e => e.Category == ExerciseCategory.Movement);
        Assert.Contains(first, e => e.Category is ExerciseCategory.Neck or ExerciseCategory.Shoulders);
        var history = first.Select(e => new ExerciseHistoryRecord("previous", e.Id, DateTimeOffset.Now, true, false)).ToArray();
        var second = selector.Select(BreakType.Full, 100 * 60, history);
        Assert.Empty(first.Select(e => e.Id).Intersect(second.Select(e => e.Id)));
        Assert.Equal(second, selector.Select(BreakType.Full, 100 * 60, history));
    }

    [Fact]
    public void RecentRepeatedMovementLowersItsPriorityWhenNotRequired()
    {
        var selector = new ExerciseSelector();
        var history = new[]
        {
            new ExerciseHistoryRecord("a",20,DateTimeOffset.Now,true,false),
            new ExerciseHistoryRecord("b",21,DateTimeOffset.Now.AddDays(-1),true,false),
            new ExerciseHistoryRecord("c",22,DateTimeOffset.Now.AddDays(-2),true,false)
        };
        var selected = selector.Select(BreakType.Quick, 30 * 60, history);
        Assert.DoesNotContain(selected, e => e.Category == ExerciseCategory.Movement);
    }

    [Fact]
    public void HealthScoreIsBoundedAndRespondsToHabits()
    {
        var healthy = new DailyStatistics(DateOnly.FromDateTime(DateTime.Now), 3600, 300, 2, 2, 1800, 1800, 4, 0, 0, 0);
        Assert.Equal(new HealthScoreResult(100, "Excellent"), HealthScore.Calculate(healthy));
        var poor = healthy with { LongestSessionSeconds = 180 * 60, BreaksSkipped = 10, EmergencySkips = 3 };
        Assert.Equal(new HealthScoreResult(39, "Poor"), HealthScore.Calculate(poor));
        Assert.True(HealthScore.Calculate(healthy with { Snoozes = 10, Breaks = 0, ExercisesCompleted = 0 }).Score < 100);
    }

    [Fact]
    public void HealthScorePenaltiesAreCappedAndGoodActionsRemainVisible()
    {
        var date = DateOnly.FromDateTime(DateTime.Now);
        var severe = new DailyStatistics(date, 8 * 3600, 0, 1, 0, 0, 8 * 3600, 0, 100, 100, 100);
        var before = HealthScore.Calculate(severe);
        var afterBreak = HealthScore.Calculate(severe with { Breaks = 1 });
        var afterExercises = HealthScore.Calculate(severe with { Breaks = 1, ExercisesCompleted = 3 });

        Assert.Equal(0, before.Score);
        Assert.True(afterBreak.Score > before.Score);
        Assert.True(afterExercises.Score > afterBreak.Score);
    }

    [Fact]
    public void SettingsValidateOrderingAndNeverPersistDemo()
    {
        Assert.Empty(new AppSettings().Validate());
        Assert.NotEmpty(new AppSettings { SecondReminderMinutes = 10 }.Validate());
        Assert.NotEmpty(new AppSettings { FullBreakSeconds = 60 }.Validate());
        var json = JsonSerializer.Serialize(new AppSettings { DemoMode = true });
        Assert.False(JsonSerializer.Deserialize<AppSettings>(json)!.DemoMode);
    }
}
