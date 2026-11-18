using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using EarthquakeWarning.Models;

namespace EarthquakeWarning.Views;

public partial class EarthquakeAlertWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Func<AlertSnapshot?> _current;
    private readonly Action _mute;
    private readonly EarthquakeNotificationSettings _settings;
    public AlertViewModel Model { get; } = new();
    public bool Muted { get; private set; }

    public EarthquakeAlertWindow() : this(() => null, () => { }) { }

    public EarthquakeAlertWindow(Func<AlertSnapshot?> current, Action mute, EarthquakeNotificationSettings? settings = null)
    {
        _current = current;
        _mute = mute;
        _settings = settings ?? new();
        InitializeComponent();
        WaveMap.LegendChanged += text => MapLegend.Text = text;
        DataContext = Model;
        _timer.Tick += (_, _) => Refresh();
        Opened += (_, _) => { Refresh(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
        KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; Close(); } };
    }

    public void ShowOnClassIslandScreen()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var host = lifetime?.MainWindow ?? lifetime?.Windows.FirstOrDefault(window => window != this && window.IsVisible);
        var screen = host is null ? Screens.Primary : Screens.ScreenFromWindow(host);
        if (screen is not null) Position = screen.Bounds.Position;
        Show();
        WindowState = WindowState.FullScreen;
        Activate();
    }

    private void Refresh()
    {
        var snapshot = _current();
        if (snapshot is null) { Close(); return; }
        Model.Update(snapshot);
        WaveMap.Configure(_settings);
        WaveMap.Update(snapshot);
    }

    private void MuteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        Muted = true;
        _mute();
        Model.MuteLabel = "已静音";
    }

    private void DismissClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => Close();
    private void ZoomIn(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => WaveMap.ZoomBy(1);
    private void ZoomOut(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => WaveMap.ZoomBy(-1);
    private void FitMap(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => WaveMap.Fit();
}
