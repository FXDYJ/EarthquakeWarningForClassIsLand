using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;

namespace EarthquakeWarning.Models;

public partial class AlertViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "地震预警";
    [ObservableProperty] private string _verification = "正在核验";
    [ObservableProperty] private string _place = "";
    [ObservableProperty] private string _magnitude = "";
    [ObservableProperty] private string _intensity = "—";
    [ObservableProperty] private string _distance = "";
    [ObservableProperty] private string _depth = "";
    [ObservableProperty] private string _time = "";
    [ObservableProperty] private string _phase = "预计震动到达本地";
    [ObservableProperty] private string _countdown = "—";
    [ObservableProperty] private string _countdownUnit = "";
    [ObservableProperty] private double _countdownFontSize = 154;
    [ObservableProperty] private string _forecastDetail = "";
    [ObservableProperty] private string _guidance = "立即就近避险";
    [ObservableProperty] private string _eventDetails = "";
    [ObservableProperty] private string _voiceStatus = "";
    [ObservableProperty] private string _muteLabel = "静音";
    [ObservableProperty] private string _impact = "";
    [ObservableProperty] private string _impactDetail = "";
    [ObservableProperty] private string _secondaryAdvice = "不要等待倒计时结束";
    [ObservableProperty] private string _evidenceSummary = "接口核验";
    [ObservableProperty] private string _clock = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EvidenceDetailsLabel))] private bool _showEvidence;
    [ObservableProperty] private bool _isDrill;
    [ObservableProperty] private IBrush _accent = Brush.Parse("#FF485A");
    [ObservableProperty] private IBrush _header = Brush.Parse("#C32335");
    [ObservableProperty] private IBrush _surface = Brush.Parse("#351019");
    [ObservableProperty] private IReadOnlyList<EvidenceRow> _evidence = Array.Empty<EvidenceRow>();
    [ObservableProperty] private IReadOnlyList<SafetyStep> _safetySteps = ProtectiveSteps;
    private SeismicReport? _preferred;
    private SeismicReport[] _previousEvidence = Array.Empty<SeismicReport>();
    private string? _eventKey;
    private bool _wasHeld;
    private string _previousVerification = "";
    private bool _previousEvidenceHeld;
    public string EvidenceDetailsLabel => ShowEvidence ? "收起详情 · D" : "报文详情 · D";

    // Simple geometric action icons: down, shield, grip. They are functional signs, not illustrations.
    private const string Down = "M12 3 V20 M5 13 L12 20 L19 13 M4 23 H20";
    private const string Shield = "M12 2 L21 6 V12 C21 18 17 21 12 24 C7 21 3 18 3 12 V6 Z M8 12 L11 15 L17 9";
    private const string Grip = "M8 12 V5 C8 2 12 2 12 5 V12 M12 8 C12 5 16 5 16 8 V13 M16 10 C16 7 20 7 20 10 V16 C20 23 13 25 9 21 L3 15 C1 12 4 10 6 13 L8 15 Z";
    private static readonly SafetyStep[] ProtectiveSteps =
    [new("伏地", "降低重心，就近躲避", Geometry.Parse(Down)), new("遮挡", "保护头部和颈部", Geometry.Parse(Shield)), new("抓牢", "抓牢桌腿等固定物", Geometry.Parse(Grip))];
    private static readonly SafetyStep[] QuietSteps =
    [new("镇静", "留意周围环境", Geometry.Parse(Shield)), new("关注", "查看官方后续信息", Geometry.Parse(Down)), new("准备", "如感到震动立即防护", Geometry.Parse(Grip))];

    public void Update(AlertSnapshot snapshot)
    {
        var report = snapshot.Report;
        var severity = snapshot.Severity;
        var remaining = snapshot.Remaining;
        var held = !snapshot.ModelSupported || snapshot.Event.HasConflict || snapshot.Event.HoldReason.Length > 0 || !snapshot.Event.Confirmed;
        var usable = snapshot.ModelSupported && !held && !snapshot.Event.Cancelled;
        if (_eventKey != snapshot.EventKey)
        {
            _eventKey = snapshot.EventKey;
            ShowEvidence = held;
            _wasHeld = held;
        }
        else if (held && !_wasHeld) ShowEvidence = true;
        _wasHeld = held;
        IsDrill = snapshot.Drill;
        Title = snapshot.Event.Cancelled ? "预警已撤销" : held ? "震情正在复核" : "地震预警";
        Verification = snapshot.Event.Verification;
        Accent = Brush.Parse(snapshot.Event.Cancelled ? "#CED8E8" : held ? "#FFBD66" : severity.Accent);
        Header = Brush.Parse(snapshot.Event.Cancelled ? "#415064" : held ? "#885414" : severity.Header);
        Surface = Brush.Parse(snapshot.Event.Cancelled ? "#1C2634" : held ? "#362813" : severity.Surface);
        Place = report.PlaceName;
        Magnitude = report.Magnitude.ToString("F1");
        Intensity = usable ? snapshot.LocalIntensity.ToString("F1") : "—";
        Distance = $"{snapshot.Distance:F0}";
        Depth = report.Depth is > 0 ? $"{report.Depth:F0}" : "未知";
        Time = report.OriginTime.ToOffset(TimeSpan.FromHours(8)).ToString("MM-dd HH:mm:ss");
        Clock = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("MM-dd HH:mm:ss");
        EventDetails = $"震中 {Math.Abs(report.Latitude):F3}°{(report.Latitude >= 0 ? "N" : "S")} / {Math.Abs(report.Longitude):F3}°{(report.Longitude >= 0 ? "E" : "W")}";
        EvidenceSummary = $"{snapshot.Event.MatchingGateways} 个接收路径 · {snapshot.Event.Reports.Count} 条证据";
        Phase = snapshot.Event.Cancelled ? "预警已撤销" : !snapshot.ModelSupported ? "数据不足，暂不估算"
            : held ? "正在复核，倒计时暂停" : remaining <= 0 ? "预计震动已到达"
            : remaining <= 10 ? "预计震动即将到达" : "预计震动到达本地";
        Countdown = !usable ? "—" : remaining > 0 ? Math.Ceiling(remaining).ToString("0") : "到达";
        CountdownUnit = usable && remaining > 0 ? "秒" : "";
        CountdownFontSize = usable && remaining <= 0 ? 86 : 154;
        ForecastDetail = usable && snapshot.Arrival is { } arrival
            ? $"预计到达 {arrival.ToOffset(TimeSpan.FromHours(8)):HH:mm:ss} · 可能提前或延后"
            : snapshot.Event.Cancelled ? "撤销不代表现场一定安全，请关注官方信息"
            : "本地烈度及到达时间暂不提供，请关注现场情况";
        Impact = !usable ? "当地影响待核验" : severity.Impact switch
        {
            AlertImpact.Weak => "预计无感或微弱震感",
            AlertImpact.Felt => "预计有明显震感",
            _ => "预计强烈震感"
        };
        ImpactDetail = !usable ? "参数不确定，暂不判断" : severity.Impact switch
        {
            AlertImpact.Weak => "持续关注后续震情",
            AlertImpact.Felt => "留意坠落物，就近防护",
            _ => "可能造成破坏，立即防护"
        };
        Guidance = snapshot.Event.Cancelled ? "关注官方后续信息" : held ? "保持防护，等待核验"
            : severity.Impact == AlertImpact.Weak ? "持续关注震情"
            : remaining > 0 ? "立即就近避险" : "继续就近避险";
        SecondaryAdvice = snapshot.Event.Cancelled ? "听从老师指挥，检查周围环境" : held ? "参数分歧或证据不足，已暂停到达播报"
            : severity.Impact == AlertImpact.Weak ? "如感到震动，立即保护自己"
            : remaining > 0 ? "不要等待倒计时结束" : "震动结束后，听从老师指挥有序撤离";
        SafetySteps = usable && severity.Impact == AlertImpact.Weak ? QuietSteps : ProtectiveSteps;
        var evidence = snapshot.Event.Reports.OrderBy(item => item.Gateway).ThenBy(item => item.Feed).ToArray();
        if (report == _preferred && evidence.SequenceEqual(_previousEvidence) && _previousVerification == Verification && _previousEvidenceHeld == held) return;
        _previousEvidenceHeld = held;
        _previousVerification = Verification;
        _preferred = report;
        _previousEvidence = evidence;
        Evidence = evidence.Select(item => new EvidenceRow(SourceName(item),
            item.Cancelled || snapshot.Event.Cancelled ? "已撤销" : evidence.Any(other => !Services.Feeds.ConsensusEngine.Compatible(item, other)) ? "分歧" : held ? "待核验" : "吻合",
            $"M {item.Magnitude:F1} · 深度 {(item.Depth is > 0 ? $"{item.Depth:F0} km" : "未知")}",
            $"{item.PlaceName} · 第 {item.ReportNumber?.ToString() ?? "—"} 报",
            $"{item.Publisher} · 发震 {item.OriginTime.ToOffset(TimeSpan.FromHours(8)):HH:mm:ss}", ShowEvidence)).ToArray();
    }

    partial void OnShowEvidenceChanged(bool value) => Evidence = Evidence.Select(row => row with { ShowDetails = value }).ToArray();

    private static string SourceName(SeismicReport report) => report.Feed switch
    {
        "miui" => "MIUI · 预警记录",
        "cenc-early" => "CENC · 直连预警",
        "cenc-bulletin" => "CENC · 正式测定",
        "cenc_eew" => "Wolfx · 中国台网",
        "sc_eew" => "Wolfx · 四川",
        "fj_eew" => "Wolfx · 福建",
        "cq_eew" => "Wolfx · 重庆",
        "jma_eew" => "Wolfx · 日本气象厅",
        _ => $"{report.Gateway} · {report.Feed}"
    };
}

public sealed record SafetyStep(string Title, string Detail, Geometry Icon);
public sealed record EvidenceRow(string Name, string Status, string Parameters, string Detail, string Publisher, bool ShowDetails = false)
{
    public IBrush StatusBrush => Brush.Parse(Status == "吻合" ? "#7CE9F5" : "#FFBD66");
}
