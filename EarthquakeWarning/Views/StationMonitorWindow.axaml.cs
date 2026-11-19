using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EarthquakeWarning.Models;

namespace EarthquakeWarning.Views;

public partial class StationMonitorWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Func<StationNetworkState> _current;
    private readonly EarthquakeNotificationSettings _settings;
    public StationMonitorViewModel Model { get; } = new();

    public StationMonitorWindow() : this(() => StationNetworkState.Disabled, new()) { }
    public StationMonitorWindow(Func<StationNetworkState> current, EarthquakeNotificationSettings settings)
    {
        _current = current; _settings = settings;
        InitializeComponent(); DataContext = Model;
        StationMap.LegendChanged += text => MapLegend.Text = text;
        _timer.Tick += (_, _) => Refresh();
        Opened += (_, _) => { Refresh(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
        KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; Close(); } };
    }

    private void Refresh()
    {
        var state = _current();
        Model.Update(state, DateTimeOffset.UtcNow);
        StationMap.Configure(_settings); StationMap.UpdateStations(state);
    }
    private void CloseClick(object? sender, RoutedEventArgs args) => Close();
    private void ZoomIn(object? sender, RoutedEventArgs args) => StationMap.ZoomBy(1);
    private void ZoomOut(object? sender, RoutedEventArgs args) => StationMap.ZoomBy(-1);
    private void FitStations(object? sender, RoutedEventArgs args) => StationMap.FitStations();
}
