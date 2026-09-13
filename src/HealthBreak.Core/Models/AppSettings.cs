using System.Text.Json.Serialization;

namespace HealthBreak.Core.Models;

public sealed class AppSettings
{
    public int IdleThresholdSeconds { get; set; } = 180;
    public int MinimumBreakSeconds { get; set; } = 120;
    public int FullBreakSeconds { get; set; } = 300;
    public int FirstReminderMinutes { get; set; } = 30;
    public int SecondReminderMinutes { get; set; } = 45;
    public int RecommendedBreakMinutes { get; set; } = 60;
    public int StrongReminderMinutes { get; set; } = 75;
    public bool ShortBreakResetsSession { get; set; }
    public int ShortBreakCreditMinutes { get; set; } = 15;
    public bool NotificationsEnabled { get; set; } = true;
    public bool NotificationSound { get; set; }
    public bool TrackApplications { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StrictMode { get; set; }
    [JsonIgnore] public bool DemoMode { get; set; }
    [JsonIgnore] public double TimeScale => DemoMode ? 60 : 1;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (IdleThresholdSeconds is < 5 or > 3600) errors.Add("Idle threshold must be between 5 and 3,600 seconds.");
        if (MinimumBreakSeconds is < 10 or > 3600) errors.Add("Minimum break must be between 10 and 3,600 seconds.");
        if (FullBreakSeconds <= MinimumBreakSeconds || FullBreakSeconds > 7200)
            errors.Add("Full break threshold must be greater than the minimum break and at most 7,200 seconds.");
        if (FirstReminderMinutes < 1 || SecondReminderMinutes <= FirstReminderMinutes ||
            RecommendedBreakMinutes <= SecondReminderMinutes || StrongReminderMinutes <= RecommendedBreakMinutes ||
            StrongReminderMinutes > 480)
            errors.Add("Reminder times must increase, starting at 1 minute and ending by 480 minutes.");
        if (ShortBreakCreditMinutes is < 0 or > 120) errors.Add("Short break credit must be between 0 and 120 minutes.");
        return errors;
    }

    public AppSettings Copy() => (AppSettings)MemberwiseClone();
}
