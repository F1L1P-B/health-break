using HealthBreak.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HealthBreak.App.Views;

public sealed partial class SettingsView : UserControl
{
    public Func<AppSettings, Task>? SaveRequested { get; set; }
    public SettingsView() => InitializeComponent();

    public void Load(AppSettings value)
    {
        First.Value = value.FirstReminderMinutes;
        Second.Value = value.SecondReminderMinutes;
        Recommended.Value = value.RecommendedBreakMinutes;
        Strong.Value = value.StrongReminderMinutes;
        Idle.Value = value.IdleThresholdSeconds;
        MinimumBreak.Value = value.MinimumBreakSeconds;
        FullBreak.Value = value.FullBreakSeconds;
        Credit.Value = value.ShortBreakCreditMinutes;
        ResetShort.IsOn = value.ShortBreakResetsSession;
        Notifications.IsOn = value.NotificationsEnabled;
        Sound.IsOn = value.NotificationSound;
        Tracking.IsOn = value.TrackApplications;
        Startup.IsOn = value.StartWithWindows;
        Strict.IsOn = value.StrictMode;
        Demo.IsOn = value.DemoMode;
        SaveMessage.IsOpen = false;
    }

    private void Defaults_Click(object sender, RoutedEventArgs args) => Load(new AppSettings());

    private async void Save_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            SaveButton.IsEnabled = false;
            var fields = new[] { First,Second,Recommended,Strong,Idle,MinimumBreak,FullBreak,Credit };
            if (fields.Any(n => !double.IsFinite(n.Value) || n.Value != Math.Truncate(n.Value)))
                throw new ArgumentException("Enter whole numbers in every time field.");
            var value = new AppSettings
            {
                FirstReminderMinutes=(int)First.Value,SecondReminderMinutes=(int)Second.Value,
                RecommendedBreakMinutes=(int)Recommended.Value,StrongReminderMinutes=(int)Strong.Value,
                IdleThresholdSeconds=(int)Idle.Value,MinimumBreakSeconds=(int)MinimumBreak.Value,
                FullBreakSeconds=(int)FullBreak.Value,ShortBreakCreditMinutes=(int)Credit.Value,
                ShortBreakResetsSession=ResetShort.IsOn,NotificationsEnabled=Notifications.IsOn,
                NotificationSound=Sound.IsOn,TrackApplications=Tracking.IsOn,StartWithWindows=Startup.IsOn,
                StrictMode=Strict.IsOn,DemoMode=Demo.IsOn
            };
            var errors = value.Validate();
            if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
            if (SaveRequested is null) throw new InvalidOperationException("Monitoring is still starting.");
            await SaveRequested(value);
            SaveMessage.Severity = InfoBarSeverity.Success;
            SaveMessage.Title = "Preferences saved";
            SaveMessage.Message = "Your new rhythm is ready.";
        }
        catch (Exception ex)
        {
            SaveMessage.Severity = InfoBarSeverity.Error;
            SaveMessage.Title = "Preferences could not be saved";
            SaveMessage.Message = ex.Message;
        }
        finally { SaveButton.IsEnabled = true; SaveMessage.IsOpen = true; }
    }
}

