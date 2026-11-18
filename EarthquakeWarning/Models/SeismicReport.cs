namespace EarthquakeWarning.Models;

public enum ReportKind { EarlyWarning, Bulletin }

/// <summary>A report keeps the publisher separate from the delivery service used to receive it.</summary>
public sealed record SeismicReport(
    string Feed, string Gateway, string Publisher, string EventId, int? ReportNumber,
    DateTimeOffset OriginTime, DateTimeOffset ReportTime, double Latitude, double Longitude,
    double Magnitude, double? Depth, string PlaceName, ReportKind Kind = ReportKind.EarlyWarning,
    bool Cancelled = false, bool Training = false, double? MaximumIntensity = null)
{
    public string Key => $"{Gateway}/{Feed}/{EventId}";
    public bool HasValidParameters => double.IsFinite(Latitude) && Latitude is >= -90 and <= 90
        && double.IsFinite(Longitude) && Longitude is >= -180 and <= 180
        && double.IsFinite(Magnitude) && Magnitude is > 0 and <= 10
        && (Latitude != 0 || Longitude != 0)
        && (Depth is null || (double.IsFinite(Depth.Value) && Depth is >= 0 and <= 800));
}

public sealed record SourceOption(string Id, string Name)
{
    public static IReadOnlyList<SourceOption> All { get; } = new[]
    {
        new SourceOption("combined", "综合核验（推荐）"),
        new SourceOption("miui", "MIUI 预警记录"),
        new SourceOption("cenc-early", "CENC 直连预警记录"),
        new SourceOption("cenc-bulletin", "CENC 正式测定"),
        new SourceOption("wolfx-live", "Wolfx 多源实时推送"),
        new SourceOption("cenc_eew", "Wolfx 中国地震台网预警"),
        new SourceOption("sc_eew", "Wolfx 四川预警"),
        new SourceOption("fj_eew", "Wolfx 福建预警"),
        new SourceOption("cq_eew", "Wolfx 重庆预警"),
        new SourceOption("jma_eew", "Wolfx 日本气象厅预警"),
        new SourceOption("cenc_eqlist", "Wolfx 中国台网地震目录"),
        new SourceOption("jma_eqlist", "Wolfx 日本地震目录")
    };
}

public sealed record FeedHealth(string Name, DateTimeOffset? LastSuccess, string Status, bool Available);

public sealed record VerifiedEarthquake(string Id, SeismicReport Preferred,
    IReadOnlyList<SeismicReport> Reports, int MatchingGateways, bool HasConflict, bool Cancelled)
{
    public bool Confirmed => !Cancelled && !HasConflict && MatchingGateways >= 2
        && Reports.Any(report => report.Kind == ReportKind.EarlyWarning && !report.Cancelled);
    public string Verification => Cancelled ? "预警已撤销"
        : HasConflict ? "参数存在分歧，继续核验"
        : Confirmed ? $"{MatchingGateways} 个接口核对通过"
        : "单一接口，等待交叉核验";
}
