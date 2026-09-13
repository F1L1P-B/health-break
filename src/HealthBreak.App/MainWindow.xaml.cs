using HealthBreak.App.Services;
using HealthBreak.App.Services.Native;
using HealthBreak.App.ViewModels;
using HealthBreak.App.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace HealthBreak.App;

public sealed partial class MainWindow : Window
{
    private readonly MonitoringService _service;
    private readonly MainViewModel _viewModel;
    private readonly SettingsView _settingsView;
    private readonly bool _background;
    private readonly string? _smokeDirectory;
    private TrayIcon? _tray;
    private BreakWindow? _breakWindow;
    private bool _exitRequested;
    private bool _initialized;
    private bool _openingBreak;

    public MainWindow(string databasePath, bool background = false, string? smokeDirectory = null)
    {
        InitializeComponent();
        AppBranding.ApplyIcon(AppWindow);
        // Child controls that consume WinUI control resources must be created only
        // after the window XAML and its XamlControlsResources are initialized.
        _settingsView = new SettingsView();
        Title = "HealthBreak · A healthier workday";
        _background = background;
        _smokeDirectory = smokeDirectory;
        _service = new MonitoringService(databasePath);
        _viewModel = new MainViewModel(_service.Catalog);
        Root.DataContext = _viewModel;
        SettingsHost.Content = _settingsView;
        _settingsView.SaveRequested = SaveSettingsAsync;
        _service.StateChanged += state => DispatcherQueue.TryEnqueue(() => ApplyState(state));
        _service.ReminderRequested += offer => DispatcherQueue.TryEnqueue(() => ShowOffer(offer));
        _service.Error += message => DispatcherQueue.TryEnqueue(() => ShowError(message));
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var width = Math.Min(1380, display.WorkArea.Width - 60);
        var height = Math.Min(960, display.WorkArea.Height - 60);
        AppWindow.MoveAndResize(new RectInt32(display.WorkArea.X + (display.WorkArea.Width-width)/2,
            display.WorkArea.Y+(display.WorkArea.Height-height)/2, width, height));
        AppWindow.Closing += OnClosing;
        Root.Loaded += OnLoaded;
        Navigate("Dashboard");
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            _tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), OnTrayAction);
            await _service.StartAsync();
            if (_exitRequested) return;
            if (_background && _tray.IsAvailable) AppWindow.Hide();
            if (_smokeDirectory is not null)
            {
                Directory.CreateDirectory(_smokeDirectory);
                File.WriteAllText(Path.Combine(_smokeDirectory, "ui-started.txt"), "WinUI initialized; monitoring and SQLite started.");
                await Task.Delay(5000);
                Navigate("Statistics");
                await Task.Delay(1000);
                Navigate("Exercises");
                await Task.Delay(1000);
                Navigate("Settings");
                await Task.Delay(1000);
                Navigate("Help");
                await Task.Delay(1000);
                File.WriteAllText(Path.Combine(_smokeDirectory, "ui-verified.txt"), "All five pages loaded without a XAML/runtime exception.");
                await RequestExitAsync();
            }
        }
        catch (Exception ex)
        {
            ShowError("HealthBreak could not start: " + ex.Message);
            WriteDiagnostic(ex);
        }
    }

    private void ApplyState(AppState state)
    {
        if (_exitRequested) return;
        _viewModel.Apply(state);
        _tray?.Update(state.Monitor.CurrentSessionSeconds, state.NextReminderSeconds, state.Monitor.IsPaused);
        _breakWindow?.Update(state);
    }

    private void ShowOffer(BreakOffer offer)
    {
        if (_exitRequested) return;
        if (_breakWindow is not null) { _breakWindow.Activate(); return; }
        _breakWindow = new BreakWindow(_service, offer, _viewModel.IsDemo);
        _breakWindow.Closed += (_, _) => _breakWindow = null;
        _breakWindow.Activate();
        if (_viewModel.State?.Settings.NotificationSound == true) NotificationSound.Play();
    }

    private async void StartBreak_Click(object sender, RoutedEventArgs args) => await OpenBreakAsync();
    private async Task OpenBreakAsync()
    {
        if (_openingBreak || _exitRequested) return;
        if (_breakWindow is not null) { _breakWindow.Activate(); return; }
        _openingBreak = true;
        try { ShowOffer(await _service.PreviewBreakAsync()); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _openingBreak = false; }
    }

    private async void Pause_Click(object sender, RoutedEventArgs args)
    {
        try { await _service.PauseAsync(_viewModel.State?.Monitor.IsPaused != true); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void Navigate_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string page }) Navigate(page);
    }

    private void Navigate(string page)
    {
        _viewModel.SetPage(page);
        DashboardPage.Visibility = page == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        StatisticsPage.Visibility = page == "Statistics" ? Visibility.Visible : Visibility.Collapsed;
        ExercisesPage.Visibility = page == "Exercises" ? Visibility.Visible : Visibility.Collapsed;
        SettingsHost.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        HelpPage.Visibility = page == "Help" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { DashboardNav,StatisticsNav,ExercisesNav,SettingsNav,HelpNav })
        {
            var selected = (string)button.Tag == page;
            button.Background = MainViewModel.Brush(selected ? 0x253D31 : 0x10171E);
            button.Foreground = MainViewModel.Brush(selected ? 0xB9EDCA : 0x95A3AF);
        }
        if (page == "Settings") _settingsView.Load(_viewModel.State?.Settings ?? new());
    }

    private async Task SaveSettingsAsync(HealthBreak.Core.Models.AppSettings settings)
    {
        if (_viewModel.State?.Monitor.IsOnBreak == true)
            throw new InvalidOperationException("Finish or end the current break before changing preferences.");
        if (_breakWindow is not null)
        {
            _breakWindow.CloseForExit();
            _breakWindow = null;
        }
        await _service.SaveSettingsAsync(settings);
    }

    private async void OnTrayAction(TrayAction action)
    {
        try
        {
            switch (action)
            {
                case TrayAction.OpenDashboard: ShowDashboard("Dashboard"); break;
                case TrayAction.Settings: ShowDashboard("Settings"); break;
                case TrayAction.StartBreak: await OpenBreakAsync(); break;
                case TrayAction.PauseMonitoring: await _service.PauseAsync(true); break;
                case TrayAction.ResumeMonitoring: await _service.PauseAsync(false); break;
                case TrayAction.Exit: await RequestExitAsync(); break;
            }
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ShowDashboard(string page)
    {
        Navigate(page);
        AppWindow.Show();
        Activate();
        NativeWindow.BringToFront(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    private async void Exit_Click(object sender, RoutedEventArgs args) => await RequestExitAsync();

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exitRequested) return;
        args.Cancel = true;
        if (_tray?.IsAvailable == true) AppWindow.Hide();
        else await RequestExitAsync();
    }

    private async Task RequestExitAsync()
    {
        if (_exitRequested) return;
        _exitRequested = true;
        _breakWindow?.CloseForExit();
        _breakWindow = null;
        _tray?.Dispose();
        _tray = null;
        try { await _service.DisposeAsync(); }
        catch (Exception ex) { WriteDiagnostic(ex); }
        Close();
        Application.Current.Exit();
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = "Something needs attention: " + message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private void WriteDiagnostic(Exception ex)
    {
        try
        {
            var directory = _smokeDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HealthBreak");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory,"startup.log"), DateTimeOffset.Now + " " + ex + Environment.NewLine);
        }
        catch { }
    }
}

