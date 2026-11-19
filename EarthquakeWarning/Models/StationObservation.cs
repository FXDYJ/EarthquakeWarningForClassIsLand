using System.Globalization;

namespace EarthquakeWarning.Models;

public sealed record StationMetadata(string Id, double Latitude, double Longitude, int Floor, string TownshipCode, bool Active);

/// <summary>TREM instrumental intensity is its own scale, never Chinese local estimated intensity.</summary>
public sealed record StationReading(string Id, double? Intensity, double? Pga, bool Triggered)
{
    public string IntensityLabel => Intensity is not { } value ? "—" : value switch
    {
        < 0.5 => "0", < 1.5 => "1", < 2.5 => "2", < 3.5 => "3", < 4.5 => "4",
        < 5 => "5弱", < 5.5 => "5强", < 6 => "6弱", < 6.5 => "6强", _ => "7"
    };
    public string PgaLabel => Pga?.ToString("F2", CultureInfo.InvariantCulture) ?? "—";
}

public sealed record StationFrame(DateTimeOffset Time, string Source, IReadOnlyDictionary<string, StationReading> Readings);

public sealed record StationNetworkState(IReadOnlyList<StationMetadata> Directory, StationFrame? Frame,
    bool Enabled, bool Connected, string Status, string DirectoryError, DateTimeOffset? DirectoryUpdated, bool Simulated = false)
{
    public const string Attribution = "数据：ExpTech Studio TREM · 非商业用途 · 非官方信息，以中央气象署发布为准";
    public static StationNetworkState Disabled { get; } = new(Array.Empty<StationMetadata>(), null, false, false, "台站观测已暂停", "", null);
    public bool IsFresh(DateTimeOffset now) => Enabled && Connected && Frame is { } frame
        && now - frame.Time <= TimeSpan.FromSeconds(5) && frame.Time - now <= TimeSpan.FromSeconds(5);
    public int VisibleCount(DateTimeOffset now) => IsFresh(now)
        ? Directory.Count(station => station.Active && Frame!.Readings.ContainsKey(station.Id)) : 0;
    public string Summary(DateTimeOffset now)
    {
        if (!Enabled) return Status;
        if (!Connected) return Status;
        if (Directory.Count == 0) return DirectoryError.Length > 0 ? "台站目录不可用 · 暂无可定位的观测" : "等待台站目录 · 暂无可定位的观测";
        if (Frame is null) return "已连接 · 等待观测快照";
        if (!IsFresh(now)) return "观测已过期或时标异常 · 暂不提供读数";
        return $"{VisibleCount(now)}/{Directory.Count(station => station.Active)} 台站有数据 · 观测 {Frame.Time.ToOffset(TimeSpan.FromHours(8)):HH:mm:ss.fff}";
    }
    public StationReading? CurrentReading(string id, DateTimeOffset now) => IsFresh(now) && Frame!.Readings.TryGetValue(id, out var reading) ? reading : null;
}
