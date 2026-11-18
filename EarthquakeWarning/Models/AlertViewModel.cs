using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;

namespace EarthquakeWarning.Models;

public partial class AlertViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "地震预警";
    [ObservableProperty] private string _verification = "正在核验";
    [ObservableProperty] private string _place = "";
    [ObservableProperty] private string _magnitude = "";
    [ObservableProperty] private string _intensity = "";
    [ObservableProperty] private string _distance = "";
    [ObservableProperty] private string _depth = "";
    [ObservableProperty] private string _time = "";
    [ObservableProperty] private string _phase = "预计强震动到达";
    [ObservableProperty] private string _countdown = "—";
    [ObservableProperty] private string _countdownUnit = "秒";
    [ObservableProperty] private string _guidance = "伏地 · 遮挡 · 手抓牢";
    [ObservableProperty] private string _eventDetails = "";
    [ObservableProperty] private string _voiceStatus = "";
    [ObservableProperty] private string _muteLabel = "静音";
    [ObservableProperty] private string _impact = "";
    [ObservableProperty] private string _secondaryAdvice = "远离窗户 · 保护头部";
    [ObservableProperty] private IBrush _accent = Brush.Parse("#FF485A");
    [ObservableProperty] private IBrush _header = Brush.Parse("#C32335");
    [ObservableProperty] private IBrush _surface = Brush.Parse("#351019");
    [ObservableProperty] private bool _isDrill;
    [ObservableProperty] private IReadOnlyList<EvidenceRow> _evidence = Array.Empty<EvidenceRow>();
    private SeismicReport? _preferred;
    private SeismicReport[] _previousEvidence = Array.Empty<SeismicReport>();

    public void Update(AlertSnapshot snapshot)
    {
        var report = snapshot.Report;
        var severity = snapshot.Severity;
        Impact = severity.Name;
        Accent = Brush.Parse(severity.Accent);
        Header = Brush.Parse(severity.Header);
        Surface = Brush.Parse(severity.Surface);
        SecondaryAdvice = severity.Impact == AlertImpact.Weak ? "持续关注后续信息"
            : severity.Impact == AlertImpact.Felt ? "远离窗户 · 留意坠落物" : "远离窗户 · 保护头部";
        IsDrill = snapshot.Drill;
        Title = snapshot.Drill ? "地震预警演练" : snapshot.Event.Cancelled ? "预警已撤销"
            : snapshot.Event.HasConflict ? "震情参数待复核" : "地震预警";
        Verification = snapshot.Event.Verification;
        Place = report.PlaceName;
        Magnitude = report.Magnitude.ToString("F1");
        Intensity = snapshot.LocalIntensity.ToString("F1");
        Distance = $"{snapshot.Distance:F0} km";
        Depth = report.Depth is > 0 ? $"{report.Depth:F0} km" : "未知 / 估算采用 10 km";
        Time = report.OriginTime.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss");
        Phase = snapshot.Event.Cancelled ? "预警已撤销" : snapshot.Event.HasConflict ? "参数分歧，暂停到达播报"
            : snapshot.Remaining > 0 ? (severity.Impact == AlertImpact.Weak ? "预计震动到达" : "预计强震动到达") : "预计震动已到达";
        Countdown = snapshot.Event.Cancelled || snapshot.Event.HasConflict ? "—"
            : snapshot.Remaining > 0 ? Math.Ceiling(snapshot.Remaining).ToString("0") : "到达";
        CountdownUnit = snapshot.Remaining > 0 && !snapshot.Event.Cancelled && !snapshot.Event.HasConflict ? "秒" : "";
        Guidance = snapshot.Event.Cancelled ? "预警撤销，请关注官方后续信息"
            : snapshot.Event.HasConflict ? "参数存在分歧，正在重新核验"
            : snapshot.Remaining > 0 ? severity.Advice
            : severity.Impact == AlertImpact.Weak ? "持续关注官方后续震情" : "保持就近避险，震动结束后有序撤离";
        EventDetails = $"纬度 {report.Latitude:F3}°  ·  经度 {report.Longitude:F3}°  ·  第 {report.ReportNumber?.ToString() ?? "—"} 报\n事件 {report.EventId}";
        var evidence = snapshot.Event.Reports.OrderBy(item => item.Gateway).ThenBy(item => item.Feed).ToArray();
        if (report == _preferred && evidence.SequenceEqual(_previousEvidence)) return;
        _preferred = report;
        _previousEvidence = evidence;
        Evidence = evidence.Select(item => new EvidenceRow($"{item.Gateway} · {item.Feed}",
            item.Cancelled ? "已撤销" : evidence.Any(other => !Services.Feeds.ConsensusEngine.Compatible(item, other)) ? "分歧" : "吻合",
            $"M {item.Magnitude:F1}  /  深度 {(item.Depth is > 0 ? $"{item.Depth:F0} km" : "未知")}",
            $"{item.PlaceName} · 第 {item.ReportNumber?.ToString() ?? "—"} 报 · {item.ReportTime.ToLocalTime():HH:mm:ss}",
            item.Publisher)).ToArray();
    }
}

public sealed record EvidenceRow(string Name, string Status, string Parameters, string Detail, string Publisher);
