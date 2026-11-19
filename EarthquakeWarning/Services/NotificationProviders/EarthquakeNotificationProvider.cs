using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using EarthquakeWarning.Calculators;
using EarthquakeWarning.Models;
using EarthquakeWarning.Services.Feeds;
using EarthquakeWarning.Services.Stations;
using EarthquakeWarning.Views;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

namespace EarthquakeWarning.Services.NotificationProviders;

[NotificationProviderInfo("B27C0AF3-C917-44DE-A61D-8010C3F3FB92", "地震预警", "\uEF5C", "多接口核验地震预警与全屏震情")]
public class EarthquakeNotificationProvider : NotificationProviderBase<EarthquakeNotificationSettings>, IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SeismicSourceService _feeds = new();
    private readonly TremStationService _stations = new();
    private readonly AlertSpeechCoordinator _speech = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Dictionary<string, DateTimeOffset> _shown = new();
    private readonly HashSet<string> _actions = new();
    private readonly Dictionary<string, string> _pending = new();
    private readonly HashSet<string> _dismissed = new();
    private readonly HashSet<string> _presented = new();
    private readonly HashSet<string> _muted = new();
    private readonly IDisposable _stopRegistration;
    private Task _monitor = Task.CompletedTask;
    private Task _stationMonitor = Task.CompletedTask;
    private volatile bool _stationEnabled;
    private StationMonitorWindow? _stationWindow;
    private volatile string _mode = "combined";
    private volatile bool _enabled = true;
    private AlertSnapshot? _active;
    private EarthquakeAlertWindow? _window;
    private ConsensusEngine? _drill;
    private DateTimeOffset _drillStart;
    private SeismicReport? _drillReport;
    private string? _speechEvent;
    private AlertAudioFocus? _audioFocus;
    private DateTimeOffset _lastHealth;
    private int _disposed;
    public static EarthquakeNotificationProvider? Current { get; private set; }

    public EarthquakeNotificationProvider(IHostApplicationLifetime lifetime)
    {
        Current = this;
        _stopRegistration = lifetime.ApplicationStopping.Register(Dispose);
        _speech.StatusChanged += SpeechStatusChanged;
        Dispatcher.UIThread.Post(() =>
        {
            if (_shutdown.IsCancellationRequested) return;
            _mode = NormalizeMode(Settings.SourceMode);
            _enabled = Settings.MonitoringEnabled;
            _stationEnabled = _enabled && Settings.EnableStationMonitoring;
            _timer.Tick += Tick;
            _timer.Start();
            _monitor = Task.Run(MonitorSelectionAsync);
            _stationMonitor = Task.Run(MonitorStationsAsync);
        });
    }

    private async Task MonitorStationsAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                if (!_stationEnabled) { await Task.Delay(250, _shutdown.Token); continue; }
                using var source = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                var run = _stations.RunAsync(source.Token);
                try
                {
                    while (_stationEnabled && !run.IsCompleted) await Task.Delay(250, _shutdown.Token);
                }
                finally
                {
                    source.Cancel();
                    await run;
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception error)
        {
            Debug.WriteLine(error);
            _stations.Store.Change(state => state with { Connected = false, Status = $"台站观测停止：{error.Message}" });
        }
    }

    public void OpenStationMonitor()
    {
        if (_disposed != 0) return;
        if (_stationWindow is not null) { _stationWindow.Topmost = _window is not null; _stationWindow.Activate(); return; }
        _stationWindow = new StationMonitorWindow(() => _stations.Snapshot, Settings);
        _stationWindow.Topmost = _window is not null;
        _stationWindow.Closed += (_, _) => _stationWindow = null;
        _stationWindow.Show();
    }

    private async Task MonitorSelectionAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                if (!_enabled) { await Task.Delay(250, _shutdown.Token); continue; }
                var mode = _mode;
                using var source = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                var run = _feeds.RunAsync(mode, source.Token);
                try
                {
                    while (!_shutdown.IsCancellationRequested && _enabled && _mode == mode && !run.IsCompleted)
                        await Task.Delay(250, _shutdown.Token);
                }
                finally
                {
                    source.Cancel();
                    try { await run; }
                    catch (OperationCanceledException) when (source.IsCancellationRequested) { }
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception error)
        {
            Debug.WriteLine(error);
            Dispatcher.UIThread.Post(() => Settings.ServerInfo = $"监测停止：{error.Message}");
        }
    }

    private void Tick(object? sender, EventArgs args)
    {
        try { Evaluate(); }
        catch (Exception error) { Debug.WriteLine(error); Settings.ServerInfo = $"预警处理异常：{error.Message}"; }
    }

    private void Evaluate()
    {
        if (_disposed != 0 || _shutdown.IsCancellationRequested) return;
        _stationEnabled = Settings.MonitoringEnabled && Settings.EnableStationMonitoring;
        Settings.StationStatus = _stations.Snapshot.Summary(DateTimeOffset.UtcNow);
        if (_stations.Snapshot.DirectoryError.Length > 0) Settings.StationStatus += " · " + _stations.Snapshot.DirectoryError;
        var mode = NormalizeMode(Settings.SourceMode);
        if (_mode != mode || _enabled != Settings.MonitoringEnabled)
        {
            _mode = mode;
            _enabled = Settings.MonitoringEnabled;
            _feeds.Invalidate();
            CloseActive();
            if (!_enabled) StopDrill();
        }
        if (!_enabled) { Settings.ServerInfo = "地震监测已暂停"; return; }
        var now = DateTimeOffset.UtcNow;
        if (now - _lastHealth > TimeSpan.FromSeconds(1))
        {
            _lastHealth = now;
            var health = _feeds.Health;
            Settings.ServerInfo = $"{health.Count(item => item.Available)}/{health.Count} 个连接可用 · {now.ToLocalTime():HH:mm:ss}";
            Settings.SourceStatus = string.Join("\n", health.Select(item => $"{item.Name}：{item.Status} · 最近成功 {item.LastSuccess?.ToLocalTime().ToString("HH:mm:ss") ?? "尚无"}"));
            var recent = _feeds.LatestReports;
            Settings.RecentReports = string.Join("\n\n", recent.Take(8).Select(item =>
                $"{item.Gateway} / {item.Feed} · {(item.Kind == ReportKind.EarlyWarning ? "预警记录" : "震后信息")}\n{item.OriginTime.ToLocalTime():MM-dd HH:mm:ss} {item.PlaceName} M{item.Magnitude:F1}\n北纬 {item.Latitude:F3}°，东经 {item.Longitude:F3}°，深度 {(item.Depth is > 0 ? $"{item.Depth:F0} km" : "未知")} · 第 {item.ReportNumber?.ToString() ?? "—"} 报"));
            if (_active is null && recent.FirstOrDefault() is { } latest)
                Settings.Info = $"最近记录：{latest.OriginTime.ToLocalTime():MM-dd HH:mm:ss} {latest.PlaceName} M{latest.Magnitude:F1}（{latest.Feed}）";
        }
        if (!ValidLocation(Settings.Latitude, Settings.Longitude))
        {
            Settings.Info = "请先设置本地经纬度，或使用自动定位。";
            CloseActive();
            return;
        }
        foreach (var key in _shown.Where(item => now - item.Value > TimeSpan.FromMinutes(6)).Select(item => item.Key).ToArray())
        { _shown.Remove(key); _dismissed.Remove(key); _pending.Remove(key); _actions.Remove(key); _presented.Remove(key); _muted.Remove(key); }
        var snapshots = _feeds.Engine.GetEvents(now, mode)
            .Select(item => new AlertSnapshot(item, Settings.Latitude, Settings.Longitude)).ToList();
        foreach (var unsupported in snapshots.Where(item => !item.ModelSupported && !item.Event.Cancelled
                     && item.Distance <= 500 && item.Event.Reports.Any(report => report.Kind == ReportKind.EarlyWarning)))
        {
            const string state = "本地烈度无法可靠估算，暂不升级";
            if (!_pending.ContainsKey(unsupported.EventKey))
            {
                _pending[unsupported.EventKey] = state;
                _shown.TryAdd(unsupported.EventKey, now);
                ShowCompact(unsupported, state);
            }
        }
        if (_drill is not null && snapshots.Any(item => item.Event.Confirmed && IsRelevant(item) && item.Remaining > -30)) StopDrill();
        if (_drill is not null && _drillReport is not null)
        {
            if (now - _drillStart > TimeSpan.FromSeconds(2))
                _drill.Add(_drillReport with { Gateway = "演练接口 B", Feed = "drill-b", Magnitude = _drillReport.Magnitude + 0.1 }, now);
            snapshots.AddRange(_drill.GetEvents(now, "combined").Select(item => new AlertSnapshot(item, Settings.Latitude, Settings.Longitude, true)));
        }
        foreach (var snapshot in snapshots.Where(item => IsRelevant(item) && item.Remaining > 0
                     && item.Event.Reports.Any(report => report.Kind == ReportKind.EarlyWarning)))
        {
            UpdateLegacyInfo(snapshot);
            if (!snapshot.Event.Confirmed)
            {
                var state = snapshot.Event.Verification;
                if (!_pending.TryGetValue(snapshot.EventKey, out var previous) || previous != state)
                {
                    _pending[snapshot.EventKey] = state;
                    _shown.TryAdd(snapshot.EventKey, now);
                    ShowCompact(snapshot, state);
                }
                continue;
            }
            if (!snapshot.Drill && snapshot.LocalIntensity > Settings.Threshold && _actions.Add(snapshot.EventKey)) _ = ExecuteActionAsync();
        }
        var selected = snapshots.Where(item => item.Event.Confirmed && IsRelevant(item) && item.Remaining > -30
                && !_dismissed.Contains(item.EventKey) && (item.Remaining > 0 || _shown.ContainsKey(item.EventKey)))
            .OrderBy(item => item.Drill).ThenByDescending(item => item.LocalIntensity).FirstOrDefault();
        if (_active is not null)
        {
            var current = snapshots.FirstOrDefault(item => item.EventKey == _active.EventKey);
            if (current?.Event.Cancelled == true) { ShowCompact(current, "预警已撤销"); CloseActive(); }
            else if (current?.Event.HasConflict == true && selected is null && current.Remaining > -30)
            {
                Volatile.Write(ref _active, current);
                _speech.Stop();
                ReleaseAudioFocus();
                _speechEvent = null;
                Settings.SpeechStatus = "参数存在分歧，语音已暂停";
                if (_window is not null) _window.Model.VoiceStatus = Settings.SpeechStatus;
                return;
            }
        }
        if (selected is null)
        {
            if (_active?.Drill == true) StopDrill();
            CloseActive();
            return;
        }
        var changed = _active?.EventKey != selected.EventKey;
        if (changed) { _speech.Stop(); _speechEvent = null; }
        Volatile.Write(ref _active, selected);
        if (!selected.Drill && !_muted.Contains(selected.EventKey) && (Settings.SuppressOtherAudio || Settings.BoostVolume))
        {
            if (_audioFocus is null) AlertSpeechCoordinator.ClearHostSpeech();
            _audioFocus ??= new AlertAudioFocus(Settings.SuppressOtherAudio, Settings.BoostVolume,
                text => Dispatcher.UIThread.Post(() => { if (_disposed == 0) Settings.AudioStatus = text; }));
        }
        else ReleaseAudioFocus();
        UpdateLegacyInfo(selected);
        _shown.TryAdd(selected.EventKey, now);
        if (_window is not null && (!Settings.EnableFullScreen || (!selected.Drill && selected.LocalIntensity <= Settings.Threshold))) CloseWindow();
        if (selected.Drill || selected.LocalIntensity > Settings.Threshold)
        {
            if (Settings.EnableFullScreen && _window is null)
            {
                _window = new EarthquakeAlertWindow(() => Volatile.Read(ref _active), Mute, Settings, () => _stations.Snapshot, OpenStationMonitor);
                _window.Model.VoiceStatus = Settings.SpeechStatus;
                _window.Closed += WindowClosed;
                _window.ShowOnClassIslandScreen();
            }
            else if (!Settings.EnableFullScreen && _presented.Add(selected.EventKey)) ShowCompact(selected, selected.Event.Verification);
        }
        if (_window is not null) _window.Model.MuteLabel = _muted.Contains(selected.EventKey) ? "已静音" : "静音";
        if (Settings.IsSpeechEnabled && (selected.Drill || selected.LocalIntensity > Settings.SpeechThreshold)
            && _speechEvent != selected.EventKey && !_muted.Contains(selected.EventKey) && (_audioFocus?.Ready.IsCompleted ?? true))
        {
            _speechEvent = selected.EventKey;
            _speech.Start(() => Volatile.Read(ref _active), Settings);
        }
        if (!Settings.IsSpeechEnabled) { _speech.Stop(); _speechEvent = null; }
        if (selected.Drill && selected.Remaining < -15) StopDrill();
    }

    private bool IsRelevant(AlertSnapshot item) => item.ModelSupported && (item.Drill || item.LocalIntensity > Settings.Threshold
        || (Settings.IsSpeechEnabled && item.LocalIntensity > Settings.SpeechThreshold));

    private void UpdateLegacyInfo(AlertSnapshot snapshot)
    {
        var report = snapshot.Report;
        Settings.Info = $"{snapshot.Event.Verification} · {report.PlaceName} M{report.Magnitude:F1} · 本地预估烈度 {snapshot.LocalIntensity:F1} · 震中距 {snapshot.Distance:F0} km";
        Settings.EarthquakeInfo.UpdateFrom(new EarthquakeInfo
        {
            Id = snapshot.EventKey, EventId = report.EventId, Latitude = report.Latitude, Longitude = report.Longitude,
            Magnitude = report.Magnitude, Depth = report.Depth, PlaceName = report.PlaceName,
            ShockTime = EarthquakeTime.Format(report.OriginTime.LocalDateTime), UpdateTime = EarthquakeTime.Format(report.ReportTime.LocalDateTime),
            Updates = report.ReportNumber ?? 0, EpiIntensity = report.MaximumIntensity ?? 0
        });
    }

    private void ShowCompact(AlertSnapshot snapshot, string state)
    {
        var mask = NotificationContent.CreateTwoIconsMask(snapshot.Drill ? "地震预警演练" : "地震信息", "\uEF5D", "\uED35");
        mask.Duration = TimeSpan.FromSeconds(2);
        mask.Color = new SolidColorBrush(Color.Parse(snapshot.Event.Confirmed ? snapshot.Severity.Header : "#B36A16"));
        mask.IsSpeechEnabled = false;
        var request = new NotificationRequest
        {
            MaskContent = mask,
            OverlayContent = new NotificationContent
            {
                Content = new TextBlock { Text = $"{state}\n{snapshot.Report.PlaceName} M{snapshot.Report.Magnitude:F1} · 本地预估烈度 {(snapshot.ModelSupported ? snapshot.LocalIntensity.ToString("F1") : "未知")}",
                    TextWrapping = TextWrapping.Wrap, FontSize = 22, Margin = new Avalonia.Thickness(18) },
                Duration = TimeSpan.FromSeconds(6), IsSpeechEnabled = false
            }
        };
        _ = ShowCompactSafeAsync(request);
    }

    private async Task ShowCompactSafeAsync(NotificationRequest request)
    {
        try { await ShowNotificationAsync(request); }
        catch (Exception error) { Settings.ServerInfo = $"CI 提醒失败：{error.Message}"; Debug.WriteLine(error); }
    }

    private async Task ExecuteActionAsync()
    {
        if (!Settings.IsCommandEnabled) return;
        try
        {
            Settings.ActionStatus = "核验通过，正在执行 CMD 命令";
            await EarthquakeActionExecutor.ExecuteCommandAsync(Settings.Command).ConfigureAwait(false);
            if (!_shutdown.IsCancellationRequested) Dispatcher.UIThread.Post(() => Settings.ActionStatus = "核验通过，CMD 命令已完成");
        }
        catch (Exception error)
        {
            if (!_shutdown.IsCancellationRequested) Dispatcher.UIThread.Post(() => Settings.ActionStatus = $"执行失败：{error.Message}");
            Debug.WriteLine(error);
        }
    }

    public void StartDrill()
    {
        StopDrill();
        if (!ValidLocation(Settings.Latitude, Settings.Longitude)) { Settings.Info = "请先设置本地位置再开始演练"; return; }
        var now = DateTimeOffset.UtcNow;
        var latitude = Math.Clamp(Settings.Latitude + 0.7, -85, 85);
        var longitude = (Settings.Longitude + 1.8 + 540) % 360 - 180;
        var distance = ConsensusEngine.Distance(Settings.Latitude, Settings.Longitude, latitude, longitude);
        var travel = HuaniaEarthQuakeCalculator.GetCountDownSeconds(12, distance);
        var origin = now.AddSeconds(Math.Min(0, 35 - travel));
        var targetIntensity = Settings.DrillProfile switch { "weak" => 1.8, "damaging" => 7.0, _ => 4.0 };
        var baseline = HuaniaEarthQuakeCalculator.GetIntensity(5, distance, 12, longitude);
        var slope = HuaniaEarthQuakeCalculator.GetIntensity(6, distance, 12, longitude) - baseline;
        var magnitude = Math.Clamp(5 + (targetIntensity - baseline) / Math.Max(0.1, slope), 1, 9.5);
        _drillReport = new SeismicReport("drill-a", "演练接口 A", "本地模拟数据", Guid.NewGuid().ToString("N"), 1,
            origin, now, latitude, longitude, magnitude, 12, "附近地区（模拟震中）");
        _drill = new ConsensusEngine(simulation: true);
        _drill.Add(_drillReport, now);
        _drillStart = now;
        Settings.IsDrillMode = true;
        Settings.ActionStatus = "演练中，外部命令已屏蔽";
    }

    public void StopDrill()
    {
        if (_active?.Drill == true) CloseActive();
        _drill = null;
        _drillReport = null;
        Settings.IsDrillMode = false;
    }

    private void Mute()
    {
        if (_active is not null) _muted.Add(_active.EventKey);
        _speech.Stop();
        ReleaseAudioFocus();
        Settings.SpeechStatus = "本次预警已静音";
    }

    private void WindowClosed(object? sender, EventArgs args)
    {
        if (_stationWindow is not null) _stationWindow.Topmost = false;
        if (_active is not null) _dismissed.Add(_active.EventKey);
        _window = null;
        _speech.Stop();
        ReleaseAudioFocus();
    }

    private void CloseActive()
    {
        CloseWindow();
        _speech.Stop();
        ReleaseAudioFocus();
        _speechEvent = null;
        Volatile.Write(ref _active, null);
    }

    private void CloseWindow()
    {
        if (_window is null) return;
        if (_stationWindow is not null) _stationWindow.Topmost = false;
        _window.Closed -= WindowClosed;
        _window.Close();
        _window = null;
    }

    private void SpeechStatusChanged(string text) => Dispatcher.UIThread.Post(() =>
    {
        if (_shutdown.IsCancellationRequested) return;
        Settings.SpeechStatus = text;
        if (_window is not null) _window.Model.VoiceStatus = text;
    });

    private void ReleaseAudioFocus() { _audioFocus?.Dispose(); _audioFocus = null; }

    private static bool ValidLocation(double latitude, double longitude) => double.IsFinite(latitude) && double.IsFinite(longitude)
        && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180 && (latitude != 0 || longitude != 0);
    private static string NormalizeMode(string value) => SourceOption.All.Any(option => option.Id == value) ? value : "combined";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        _speech.Dispose();
        ReleaseAudioFocus();
        _stopRegistration.Dispose();
        Dispatcher.UIThread.Post(() => { _timer.Stop(); CloseActive(); _stationWindow?.Close(); });
        _ = ReleaseAsync();
        if (Current == this) Current = null;
    }

    private async Task ReleaseAsync()
    {
        try { await Task.WhenAll(_monitor, _stationMonitor); }
        finally { _feeds.Dispose(); _stations.Dispose(); _shutdown.Dispose(); }
    }
}
