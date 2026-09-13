using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HealthBreak.App.Services;
using HealthBreak.Core.Models;
using HealthBreak.Core.Services;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HealthBreak.App.ViewModels;

public sealed record WeekColumn(string Day, string ValueLabel, double Height, SolidColorBrush Brush);
public sealed record WeekRow(string Day, string Active, string Idle, int Sessions, int Breaks,
    string Average, string Longest, int Exercises, int Skipped);
public sealed record LibraryExercise(int Id, string Name, string Category, string Description, string DurationLabel);

public sealed class MainViewModel : ObservableObject
{
    private AppState? _state;
    private string _page = "Dashboard";
    public AppState? State => _state;
    public string PageTitle => _page switch { "Statistics" => "Your activity, over time", "Exercises" => "The exercise library", "Settings" => "Make it your rhythm", "Help" => "How HealthBreak works", _ => "A healthier workday." };
    public string PageEyebrow => _page == "Dashboard" ? DateTime.Now.ToString("dddd, dd MMMM", CultureInfo.GetCultureInfo("en-US")).ToUpperInvariant() : "HEALTHBREAK / " + _page.ToUpperInvariant();
    public string PageSubtitle => _page switch { "Statistics" => "Understand your habits. Make space for better ones.", "Exercises" => "Simple, gentle exercises for your next pause.", "Settings" => "Your reminders, your pace. Everything stays on this device.", "Help" => "A quick guide to monitoring, breaks, scores, privacy and recovery.", _ => "Stay focused. Take a breath. Find your balance." };
    public string MonitoringStatus => _state is null ? "Starting…" : _state.Monitor.IsPaused ? "Monitoring paused" : _state.Monitor.IsOnBreak ? "Taking a break" : _state.Monitor.IsIdle ? "Away · idle" : "Monitoring active";
    public string PauseButtonText => _state?.Monitor.IsPaused == true ? "Resume" : "Pause";
    public bool IsDemo => _state?.Settings.DemoMode == true;
    public string SessionTime => Clock(_state?.Monitor.CurrentSessionSeconds ?? 0);
    public string ActiveToday => Duration(_state?.Today.ActiveSeconds ?? 0);
    public int BreaksToday => _state?.Today.Breaks ?? 0;
    public int ExercisesCompleted => _state?.Today.ExercisesCompleted ?? 0;
    public string LongestSession => Duration(_state?.Today.LongestSessionSeconds ?? 0);
    public string AverageSessionText => "Average · " + Duration(_state?.Today.AverageSessionSeconds ?? 0);
    public string SkippedText => $"{_state?.Today.BreaksSkipped ?? 0} skipped reminders";
    public string SinceBreakText => Duration(_state?.Monitor.TimeSinceLastBreakSeconds ?? 0) + " active since last break";
    public double SessionProgress => Math.Clamp((_state?.Monitor.CurrentSessionSeconds ?? 0) /
        ((_state?.Settings.RecommendedBreakMinutes ?? 60) * 60.0) * 100, 0, 100);
    public string NextBreakText => _state?.Monitor.IsOnBreak == true ? "Break in progress · " + Clock(_state.Monitor.BreakRemainingSeconds) :
        _state?.NextReminderSeconds > 0 ? "Next reminder in " + Duration(_state.NextReminderSeconds) : "A health break is recommended";
    public string SessionMessage => _state?.Monitor.IsPaused == true ? "Your timer is paused. Resume when you are ready." :
        _state?.Monitor.IsOnBreak == true ? "This time is for you. Your work timer can wait." :
        _state?.Monitor.IsIdle == true ? "You are away. Idle time is excluded from active work." :
        (_state?.Monitor.CurrentSessionSeconds ?? 0) >= 3600 ? "You've earned a pause. Give your body a little attention." : "A good rhythm starts with making time for a pause.";
    private HealthScoreResult Balance => HealthScore.Calculate(_state?.Today ?? new(DateOnly.FromDateTime(DateTime.Now),0,0,0,0,0,0,0,0,0,0), _state?.Monitor.CurrentSessionSeconds ?? 0);
    public int Score => Balance.Score;
    public string ScoreStatus => Balance.Status;
    public SolidColorBrush ScoreBrush => Brush(Score >= 80 ? 0xB9EDCA : Score >= 60 ? 0xC5DFA5 : Score >= 40 ? 0xF0CC8A : 0xF29B9B);
    public string ScoreMessage => Score >= 80 ? "A little consistency goes a long way." : "Your next break is a good place to begin.";
    public string SuggestedExerciseName => _state?.SuggestedExercise.Name ?? "Look beyond your screen";
    public string SuggestedExerciseDescription => _state?.SuggestedExercise.Description ?? "Let your eyes rest on something in the distance.";
    public string SuggestedExerciseMeta => _state is null ? "Eyes · 20 seconds" : $"{_state.SuggestedExercise.Category}  ·  {_state.SuggestedExercise.DurationSeconds} seconds";
    public string CurrentAppText => _state?.Settings.TrackApplications == true ? $"Now · {_state.Monitor.CurrentProcess ?? "unavailable"} / {_state.Monitor.CurrentCategory ?? "Other"}" : "Application tracking is off";
    public string StatisticsSummary => _state is null ? "No activity recorded yet." :
        $"{Duration(_state.Week.Sum(d => d.ActiveSeconds))} active this week   ·   {_state.Week.Sum(d => d.Breaks)} completed breaks   ·   {_state.Week.Sum(d => d.ExercisesCompleted)} exercises completed";
    public string ReminderHabits => _state is null ? "No reminders yet." :
        $"Today: {_state.Today.Snoozes} snoozes · {_state.Today.BreaksSkipped} skips · {_state.Today.EmergencySkips} emergency skips.\n\nCompleting a break and confirming exercises supports your daily score.";
    public string ApplicationSummary => _state?.Settings.TrackApplications != true ? "Application tracking is disabled. You can enable local category totals in Settings." :
        _state.Applications.Count == 0 ? "Categories appear after input is detected in an active application." :
        string.Join("\n", _state.Applications.OrderByDescending(p => p.Value).Select(p => $"{p.Key}   {Duration(p.Value)}")) + "\n\nTotals reflect samples with fresh input.";
    public IReadOnlyList<LibraryExercise> Library { get; }
    public IReadOnlyList<WeekColumn> WeekColumns { get; private set; } = [];
    public IReadOnlyList<WeekRow> WeekRows { get; private set; } = [];

    public MainViewModel(IReadOnlyList<Exercise> catalog) => Library = catalog.Select(e =>
        new LibraryExercise(e.Id,e.Name,e.Category == ExerciseCategory.Movement ? "Movement" : e.Category.ToString(),
            e.Description,$"{e.DurationSeconds} sec" + (e.Repetitions is {} r ? $"\n{r} reps" : ""))).ToArray();

    public void SetPage(string page) { _page = page; OnPropertyChanged(nameof(PageTitle)); OnPropertyChanged(nameof(PageEyebrow)); OnPropertyChanged(nameof(PageSubtitle)); }

    public void Apply(AppState state)
    {
        _state = state;
        var max = Math.Max(1, state.Week.Max(d => d.ActiveSeconds));
        WeekColumns = state.Week.Select(d => new WeekColumn(d.Date.ToString("ddd",CultureInfo.GetCultureInfo("en-US")),
            Duration(d.ActiveSeconds), Math.Max(2, d.ActiveSeconds / max * 102),
            Brush(d.Date == DateOnly.FromDateTime(DateTime.Now) ? 0xB9EDCA : 0x668F7B))).ToArray();
        WeekRows = state.Week.Reverse().Select(d => new WeekRow(d.Date.ToString("ddd, dd MMM",CultureInfo.GetCultureInfo("en-US")),
            Duration(d.ActiveSeconds),Duration(d.IdleSeconds),d.Sessions,d.Breaks,Duration(d.AverageSessionSeconds),
            Duration(d.LongestSessionSeconds),d.ExercisesCompleted,d.BreaksSkipped)).ToArray();
        OnPropertyChanged(string.Empty);
    }

    public static string Clock(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes:00}:{time.Seconds:00}";
    }
    public static string Duration(double seconds) => seconds < 60 ? $"{(int)Math.Max(0,seconds)}s" :
        seconds < 3600 ? $"{(int)(seconds/60)} min" : $"{(int)(seconds/3600)}h {(int)(seconds%3600/60):00}m";
    public static SolidColorBrush Brush(int rgb) => new(Color.FromArgb(255,(byte)((uint)rgb>>16),(byte)((uint)rgb>>8),(byte)rgb));
}

