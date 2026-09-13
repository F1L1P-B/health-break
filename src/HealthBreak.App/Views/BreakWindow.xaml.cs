using HealthBreak.App.Services;
using HealthBreak.App.ViewModels;
using HealthBreak.Core.Models;
using HealthBreak.Core.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace HealthBreak.App.Views;

public sealed partial class BreakWindow : Window
{
    private readonly MonitoringService _service;
    private BreakOffer _offer;
    private readonly Dictionary<int, Button> _exerciseButtons = [];
    private bool _initializing;
    private bool _started;
    private bool _handled;
    private bool _closing;
    private bool _busy;

    public BreakWindow(MonitoringService service, BreakOffer offer, bool demo)
    {
        InitializeComponent();
        AppBranding.ApplyIcon(AppWindow);
        _service = service;
        _offer = offer;
        Title = offer.IsStrict ? "Health break required · HealthBreak" : "Your health break · HealthBreak";
        AppWindow.Resize(new SizeInt32(560, 850));
        DemoLabel.Visibility = demo ? Visibility.Visible : Visibility.Collapsed;
        if (offer.IsStrict)
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            StrictHint.Visibility = Visibility.Visible;
        }
        else if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = true;
        LoadOffer(offer);
        AppWindow.Closing += OnClosing;
    }

    private void LoadOffer(BreakOffer offer)
    {
        _offer = offer;
        Heading.Text = offer.Title;
        Description.Text = offer.Message;
        Countdown.Text = MainViewModel.Clock(SessionManager.BreakDuration(offer.Type));
        _initializing = true;
        TypeSelector.SelectedIndex = (int)offer.Type;
        _initializing = false;
        TypeSelector.IsEnabled = !offer.IsStrict;
        SkipButton.Content = offer.IsStrict ? "Emergency skip" : offer.CountsAsReminder ? "Skip" : "Close";
        SnoozeButton.Visibility = offer.CountsAsReminder && !offer.IsStrict
            ? Visibility.Visible : Visibility.Collapsed;
        ExercisePanel.Children.Clear();
        _exerciseButtons.Clear();
        foreach (var exercise in offer.Exercises)
        {
            var layout = new Grid { ColumnSpacing = 12 };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Spacing = 5 };
            text.Children.Add(new TextBlock { Text = exercise.Name, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = exercise.Description, FontSize = 12, Foreground = MainViewModel.Brush(0xA1B5AA), TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = $"{exercise.Category} · {exercise.DurationSeconds} sec" + (exercise.Repetitions is {} r ? $" · {r} reps" : ""), FontSize = 11, Foreground = MainViewModel.Brush(0xB9EDCA) });
            layout.Children.Add(text);
            var button = new Button { Content = "Done", Tag = exercise.Id, IsEnabled = false, VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["QuietButton"] };
            button.Click += Exercise_Click;
            Grid.SetColumn(button, 1);
            layout.Children.Add(button);
            _exerciseButtons[exercise.Id] = button;
            ExercisePanel.Children.Add(new Border { Background = MainViewModel.Brush(0x1B2821), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = layout });
        }
    }

    public void Update(AppState state)
    {
        DemoLabel.Visibility = state.Settings.DemoMode ? Visibility.Visible : Visibility.Collapsed;
        if (!_started) return;
        if (!state.Monitor.IsOnBreak) return;
        Countdown.Text = MainViewModel.Clock(Math.Ceiling(state.Monitor.BreakRemainingSeconds));
        BreakProgress.Value = 100 - state.Monitor.BreakRemainingSeconds / SessionManager.BreakDuration(_offer.Type) * 100;
        FinishButton.IsEnabled = state.Monitor.BreakRemainingSeconds <= 0;
        PhaseLabel.Text = state.Monitor.BreakRemainingSeconds <= 0 ? "YOUR BREAK IS READY TO COMPLETE" : "BREATHE. STRETCH. RESET.";
        TimerCaption.Text = state.Monitor.BreakRemainingSeconds <= 0 ? "Complete your break whenever you are ready." : "Your active work timer is resting too.";
        CompletedCount.Text = $"{state.CompletedExerciseIds.Count} / {_offer.Exercises.Count} completed";
        foreach (var (id, button) in _exerciseButtons)
        {
            var done = state.CompletedExerciseIds.Contains(id);
            button.Content = done ? "✓ Done" : "Done";
            button.IsEnabled = !done;
        }
    }

    public void CloseForExit() { _handled = true; Close(); }

    private async void Type_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_initializing || _started || TypeSelector.SelectedIndex < 0) return;
        await Run(async () => LoadOffer(await _service.PreviewBreakAsync(
            (BreakType)TypeSelector.SelectedIndex, _offer.CountsAsReminder)));
    }

    private async void Start_Click(object sender, RoutedEventArgs args) => await Run(async () =>
    {
        _started = true;
        try { await _service.BeginBreakAsync(_offer); }
        catch { _started = false; throw; }
        StartButton.Visibility = Visibility.Collapsed;
        FinishButton.Visibility = Visibility.Visible;
        TypeSelector.Visibility = Visibility.Collapsed;
        SnoozeButton.Visibility = Visibility.Collapsed;
        SkipButton.Content = _offer.IsStrict ? "Emergency skip" : "End break early";
        foreach (var button in _exerciseButtons.Values) button.IsEnabled = true;
    });

    private async void Exercise_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: int id }) await Run(() => _service.CompleteExerciseAsync(id));
    }

    private async void Finish_Click(object sender, RoutedEventArgs args) => await Run(async () =>
    {
        await _service.FinishBreakAsync(true);
        _handled = true;
        Close();
    });

    private async void Snooze_Click(object sender, RoutedEventArgs args) => await Run(async () =>
    {
        await _service.SnoozeAsync();
        _handled = true;
        Close();
    });

    private async void Skip_Click(object sender, RoutedEventArgs args) => await Run(async () =>
    {
        await DismissAsync();
        _handled = true;
        Close();
    });

    private Task DismissAsync() => _started
        ? _service.FinishBreakAsync(false, _offer.IsStrict, _offer.CountsAsReminder)
        : _service.SkipAsync(_offer.IsStrict, _offer.CountsAsReminder);

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_handled) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        try { await DismissAsync(); }
        catch (Exception ex) { ErrorBar.Message = ex.Message; }
        finally { _handled = true; Close(); }
    }

    private async Task Run(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        try { await action(); }
        catch (Exception ex) { ErrorBar.Title = "This action could not be completed"; ErrorBar.Message = ex.Message; ErrorBar.IsOpen = true; }
        finally { _busy = false; }
    }
}

