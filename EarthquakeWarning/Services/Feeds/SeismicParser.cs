using System.Globalization;
using System.Text.Json;
using EarthquakeWarning.Models;

namespace EarthquakeWarning.Services.Feeds;

public static class SeismicParser
{
    public static IReadOnlyList<SeismicReport> Parse(string feed, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (feed == "miui") return ParseMiui(root);
        if (feed.StartsWith("cenc-", StringComparison.Ordinal)) return ParseCenc(root, feed);
        if (feed == "wolfx-live") feed = Text(root, "type") ?? "unknown";
        if (feed is "heartbeat" or "pong" or "unknown") return Array.Empty<SeismicReport>();
        if (feed is "cenc_eqlist" or "jma_eqlist") return ParseList(root, feed);
        if (feed is not ("cenc_eew" or "sc_eew" or "fj_eew" or "cq_eew" or "jma_eew"))
            return Array.Empty<SeismicReport>();
        var japan = feed == "jma_eew";
        var origin = Time(root, "OriginTime", japan ? 9 : 8);
        var issued = Time(root, japan ? "AnnouncedTime" : "ReportTime", japan ? 9 : 8);
        var id = Text(root, "EventID") ?? Text(root, "ID");
        if (origin is null || id is null) return Array.Empty<SeismicReport>();
        var report = new SeismicReport(feed, "Wolfx", Publisher(feed), id,
            Integer(root, japan ? "Serial" : "ReportNum"), origin.Value, issued ?? origin.Value,
            Number(root, "Latitude") ?? double.NaN, Number(root, "Longitude") ?? double.NaN,
            Number(root, "Magnitude") ?? Number(root, "Magunitude") ?? double.NaN,
            PositiveDepth(root, "Depth"), Text(root, japan ? "Hypocenter" : "HypoCenter") ?? "未知震中",
            Cancelled: Flag(root, "isCancel"), Training: Flag(root, "isTraining") || Flag(root, "isAssumption"),
            MaximumIntensity: Number(root, "MaxIntensity"));
        return new[] { report };
    }

    private static IReadOnlyList<SeismicReport> ParseMiui(JsonElement root)
    {
        CheckCode(root);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new JsonException("MIUI 缺少 data 列表");
        var reports = new List<SeismicReport>();
        foreach (var item in data.EnumerateArray())
        {
            var origin = Unix(item, "startAt", true);
            var updated = Unix(item, "updateAt", true);
            var id = Text(item, "eventId");
            if (origin is null || updated is null || string.IsNullOrEmpty(id) || id == "0") continue;
            reports.Add(new SeismicReport("miui", "MIUI", Text(item, "signature") ?? "小米预警服务",
                id, Integer(item, "update"), origin.Value, updated.Value,
                Number(item, "latitude") ?? double.NaN, Number(item, "longitude") ?? double.NaN,
                Number(item, "magnitude") ?? double.NaN, PositiveDepth(item, "depth"),
                Text(item, "epicenter") ?? "未知震中"));
        }
        return reports;
    }

    private static IReadOnlyList<SeismicReport> ParseCenc(JsonElement root, string feed)
    {
        CheckCode(root);
        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("spot_infos", out var items)
            || items.ValueKind != JsonValueKind.Array) throw new JsonException("CENC 缺少 spot_infos 列表");
        var reports = new List<SeismicReport>();
        foreach (var item in items.EnumerateArray())
        {
            var origin = Unix(item, "created_at", false);
            var id = Text(item, "third_id") ?? Text(item, "id");
            if (origin is null || id is null) continue;
            reports.Add(new SeismicReport(feed, "CENC", "中国地震台网", id,
                Integer(item, "serial_number"), origin.Value, origin.Value,
                Number(item, "latitude") ?? double.NaN, Number(item, "longitude") ?? double.NaN,
                Number(item, "level") ?? double.NaN, PositiveDepth(item, "depth"),
                Text(item, "location") ?? "未知震中",
                feed == "cenc-early" ? ReportKind.EarlyWarning : ReportKind.Bulletin,
                MaximumIntensity: Number(item, "epicenter_intensity")));
        }
        return reports;
    }

    private static IReadOnlyList<SeismicReport> ParseList(JsonElement root, string feed)
    {
        var reports = new List<SeismicReport>();
        foreach (var property in root.EnumerateObject())
        {
            var item = property.Value;
            if (item.ValueKind != JsonValueKind.Object) continue;
            var origin = Time(item, "time_full", feed == "jma_eqlist" ? 9 : 8)
                ?? Time(item, "time", feed == "jma_eqlist" ? 9 : 8);
            if (origin is null) continue;
            var lat = Number(item, "latitude") ?? double.NaN;
            var lon = Number(item, "longitude") ?? double.NaN;
            var id = Text(item, "EventID") ?? $"{origin.Value.ToUnixTimeSeconds()}-{lat:R}-{lon:R}";
            reports.Add(new SeismicReport(feed, "Wolfx", Publisher(feed), id, null, origin.Value,
                Time(item, "ReportTime", feed == "jma_eqlist" ? 9 : 8) ?? origin.Value, lat, lon,
                Number(item, "magnitude") ?? double.NaN, PositiveDepth(item, "depth"),
                Text(item, "placeName") ?? Text(item, "location") ?? "未知震中", ReportKind.Bulletin));
        }
        return reports;
    }

    private static string Publisher(string feed) => feed switch
    {
        "sc_eew" => "四川省地震局", "fj_eew" => "福建省地震局", "cq_eew" => "重庆市地震局",
        "jma_eew" or "jma_eqlist" => "日本气象厅", _ => "中国地震台网"
    };

    private static void CheckCode(JsonElement root)
    {
        if (Integer(root, "code") != 0) throw new JsonException($"接口业务错误 {Text(root, "code")} {Text(root, "desc") ?? Text(root, "msg")}");
    }

    private static string? Text(JsonElement item, string name) => item.TryGetProperty(name, out var value)
        && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? value.ToString() : null;
    private static double? Number(JsonElement item, string name) => double.TryParse(Text(item, name)?.Replace("km", ""),
        NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
    private static int? Integer(JsonElement item, string name) => int.TryParse(Text(item, name), out var value) ? value : null;
    private static bool Flag(JsonElement item, string name) => bool.TryParse(Text(item, name), out var value) && value;
    private static double? PositiveDepth(JsonElement item, string name) => Number(item, name) is > 0 and <= 800 ? Number(item, name) : null;

    private static DateTimeOffset? Time(JsonElement item, string name, int offset)
    {
        var text = Text(item, name);
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var time)) return null;
        return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Unspecified), TimeSpan.FromHours(offset));
    }

    private static DateTimeOffset? Unix(JsonElement item, string name, bool milliseconds)
    {
        if (!long.TryParse(Text(item, name), out var value) || value <= 0) return null;
        try { return milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(value) : DateTimeOffset.FromUnixTimeSeconds(value); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
