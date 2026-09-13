using HealthBreak.Core.Models;
using HealthBreak.Core.Services;
using Xunit;

namespace HealthBreak.Core.Tests;

public sealed class SessionManagerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 12, 12, 0, 0, TimeSpan.FromHours(2));
    private readonly SessionManager _manager = new();
    private readonly AppSettings _settings = new();

    [Fact]
    public void ActiveInputAccruesExactlyOnce()
    {
        Work(0, 100);
        Assert.Equal(100, _manager.Current.CurrentSessionSeconds, 4);
        Assert.Equal(100, _manager.Current.ActiveSecondsToday, 4);
        Assert.Equal(0, _manager.Current.IdleSecondsToday);
        Assert.Single(_manager.DrainChanges().Sessions);
        Assert.Empty(_manager.DrainChanges().Sessions);
    }

    [Fact]
    public void IdleThresholdReclassifiesEntireGracePeriod()
    {
        Work(0, 40);
        Idle(40, 179);
        Assert.Equal(219, _manager.Current.ActiveSecondsToday, 4);
        Tick(220, 180);
        Assert.Equal(40, _manager.Current.ActiveSecondsToday, 4);
        Assert.Equal(180, _manager.Current.IdleSecondsToday, 4);
        Assert.Equal(40, _manager.Current.CurrentSessionSeconds, 4);
        Assert.True(_manager.Current.IsIdle);
    }

    [Fact]
    public void ShortBreakBeforeIdleThresholdStillCountsOnReturn()
    {
        Work(0, 1800);
        Idle(1800, 149);
        Tick(1950, 0);
        Assert.Equal(1800, _manager.Current.ActiveSecondsToday, 4);
        Assert.Equal(150, _manager.Current.IdleSecondsToday, 4);
        Assert.Equal(900, _manager.Current.CurrentSessionSeconds, 4);
        var record = Assert.Single(_manager.DrainChanges().Breaks);
        Assert.Equal(BreakType.Short, record.Type);
        Assert.Equal(150, record.DurationSeconds, 4);
        Work(1951, 1960);
        Assert.Empty(_manager.DrainChanges().Breaks);
    }

    [Fact]
    public void FullNaturalBreakResetsContinuousSession()
    {
        Work(0, 2400);
        Idle(2400, 419);
        Tick(2820, 0);
        Assert.Equal(2400, _manager.Current.ActiveSecondsToday, 4);
        Assert.Equal(420, _manager.Current.IdleSecondsToday, 4);
        Assert.Equal(0, _manager.Current.CurrentSessionSeconds);
        Assert.Equal(BreakType.Full, Assert.Single(_manager.DrainChanges().Breaks).Type);
    }

    [Theory]
    [InlineData(119, 0)]
    [InlineData(120, 1)]
    [InlineData(300, 1)]
    [InlineData(301, 1)]
    public void NaturalBreakBoundariesArePrecise(int seconds, int expected)
    {
        Work(0, 20);
        Idle(20, seconds - 1);
        Tick(20 + seconds, 0);
        var breaks = _manager.DrainChanges().Breaks;
        Assert.Equal(expected, breaks.Count);
        if (seconds == 300) Assert.Equal(BreakType.Short, breaks[0].Type);
        if (seconds == 301) Assert.Equal(BreakType.Full, breaks[0].Type);
    }

    [Fact]
    public void PausedTimeAndTimeAfterSuspendAreNeverActive()
    {
        Work(0, 30);
        _manager.SetPaused(true, Start.AddSeconds(30));
        Tick(200, 0);
        _manager.SetPaused(false, Start.AddSeconds(200));
        Work(201, 210);
        Assert.Equal(40, _manager.Current.ActiveSecondsToday, 4);
        Tick(810, 0);
        Assert.Equal(40, _manager.Current.ActiveSecondsToday, 4);
        Assert.Equal(600, _manager.Current.IdleSecondsToday, 4);
        Assert.Single(_manager.DrainChanges().Breaks);
    }

    [Fact]
    public void UnavailableSamplesAreIdleAndReturnCreatesOneBreak()
    {
        Work(0, 20);
        for (var i = 21; i <= 220; i++) _manager.Tick(new(Start.AddSeconds(i), 0, false), _settings);
        Tick(221, 0);
        Assert.Equal(20, _manager.Current.ActiveSecondsToday, 4);
        Assert.Single(_manager.DrainChanges().Breaks);
    }

    [Fact]
    public void DailyRecordsSplitAtMidnightAndRetroactiveIdleCanCrossIt()
    {
        var midnight = new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.FromHours(2));
        _manager.Tick(new(midnight.AddSeconds(-100), 0, true), _settings);
        for (var i = 1; i <= 200; i++)
            _manager.Tick(new(midnight.AddSeconds(-100 + i), i, true), _settings);
        var sessions = _manager.DrainChanges().Sessions.OrderBy(s => s.StartTime).ToArray();
        Assert.Equal(2, sessions.Length);
        Assert.All(sessions, s => Assert.Equal(0, s.ActiveSeconds));
        Assert.Equal(100, sessions[0].IdleSeconds, 4);
        Assert.Equal(100, sessions[1].IdleSeconds, 4);
        Assert.Equal(midnight, sessions[0].EndTime);
        Assert.Equal(100, _manager.Current.IdleSecondsToday, 4);
    }

    [Fact]
    public void StartupIdleAgeContributesToThresholdButDoesNotImportHistoricTime()
    {
        Tick(0, 100);
        for (var i = 1; i <= 80; i++) Tick(i, 100 + i);
        Assert.Equal(0, _manager.Current.ActiveSecondsToday);
        Assert.Equal(80, _manager.Current.IdleSecondsToday, 4);
    }

    [Fact]
    public void ExplicitBreakIsPersistedImmediatelyAndCannotCompleteEarly()
    {
        Work(0, 20);
        var id = _manager.BeginBreak(BreakType.Quick, _settings, Start.AddSeconds(20));
        Assert.Equal(id, Assert.Single(_manager.DrainChanges().Breaks).Id);
        var record = _manager.FinishBreak(Start.AddSeconds(30), true);
        Assert.NotNull(record);
        Assert.False(record.Completed);
        Assert.True(record.Skipped);
        Assert.Equal(20, _manager.Current.CurrentSessionSeconds, 4);
    }

    [Fact]
    public void CompletedGuidedBreakAlwaysResetsContinuousSession()
    {
        Work(0, 600);
        var generation = _manager.Current.SessionGeneration;
        _manager.BeginBreak(BreakType.Quick, _settings, Start.AddSeconds(600));

        var record = _manager.FinishBreak(Start.AddSeconds(645), true);

        Assert.NotNull(record);
        Assert.True(record.Completed);
        Assert.Equal(0, _manager.Current.CurrentSessionSeconds);
        Assert.Equal(0, _manager.Current.TimeSinceLastBreakSeconds);
        Assert.True(_manager.Current.SessionGeneration > generation);
        Assert.Equal(600, _manager.Current.ActiveSecondsToday, 4);
    }

    [Fact]
    public void DemoScalesSessionAndBreakClockAndRemainsSeparate()
    {
        _settings.DemoMode = true;
        Work(0, 60);
        Assert.Equal(3600, _manager.Current.CurrentSessionSeconds, 4);
        _manager.BeginBreak(BreakType.Full, _settings, Start.AddSeconds(60));
        Tick(64, 0);
        Assert.Equal(60, _manager.Current.BreakRemainingSeconds, 4);
        var record = _manager.FinishBreak(Start.AddSeconds(65), true);
        Assert.NotNull(record);
        Assert.True(record.Completed);
        Assert.True(record.IsDemo);
        Assert.Equal(300, record.DurationSeconds, 4);
        Assert.Equal(0, _manager.Current.CurrentSessionSeconds);
    }

    [Fact]
    public void BackwardClockSamplesCannotSubtractOrDuplicateWork()
    {
        Work(0, 20);
        Tick(10, 0);
        Tick(21, 0);
        Assert.Equal(21, _manager.Current.ActiveSecondsToday, 4);
    }

    private void Tick(int second, double idle) => _manager.Tick(new(Start.AddSeconds(second), idle, true), _settings);
    private void Work(int from, int to) { for (var second = from; second <= to; second++) Tick(second, 0); }
    private void Idle(int from, int duration) { for (var second = 1; second <= duration; second++) Tick(from + second, second); }
}
