using System.Collections.Frozen;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using EarthquakeWarning.Models;

namespace EarthquakeWarning.Services.Stations;

public static class TremStationParser
{
    public const int MaximumMessageLength = 256 * 1024;
    public const int MaximumDecodedLength = 512 * 1024;

    public static IReadOnlyList<StationMetadata> ParseDirectory(string csv)
    {
        if (csv.Length > MaximumMessageLength) throw new InvalidDataException("台站目录过大");
        var lines = csv.TrimStart('\uFEFF').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) throw new InvalidDataException("台站目录为空");
        var headers = lines[0].Split(',');
        int Column(string key) => Array.IndexOf(headers, key) is var index && index >= 0 ? index : throw new InvalidDataException($"台站目录缺少 {key}");
        var id = Column("id"); var lat = Column("lat"); var lon = Column("lon");
        var floor = Column("floor"); var code = Column("code"); var work = Column("work");
        var stations = new Dictionary<string, StationMetadata>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split(',');
            if (fields.Length != headers.Length || !ValidId(fields[id])
                || !double.TryParse(fields[lat], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                || !double.TryParse(fields[lon], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
                || !double.IsFinite(latitude) || latitude is < -90 or > 90 || !double.IsFinite(longitude) || longitude is < -180 or > 180
                || !int.TryParse(fields[floor], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)
                || fields[work] is not ("0" or "1")) throw new InvalidDataException("台站目录含无效记录");
            if (!stations.TryAdd(fields[id], new(fields[id], latitude, longitude, level, fields[code], fields[work] == "1")))
                throw new InvalidDataException("台站目录含重复设备编号");
        }
        if (stations.Count is 0 or > 4096) throw new InvalidDataException("台站目录数量异常");
        return Array.AsReadOnly(stations.Values.OrderBy(station => station.Id, StringComparer.Ordinal).ToArray());
    }

    public static StationFrame DecodeFrame(string encoded)
    {
        if (encoded.Length > MaximumMessageLength) throw new InvalidDataException("观测消息过大");
        var compressed = Convert.FromBase64String(encoded);
        using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = gzip.Read(buffer)) > 0)
        {
            if (output.Length + count > MaximumDecodedLength) throw new InvalidDataException("解压后的观测消息过大");
            output.Write(buffer, 0, count);
        }
        return ParseFrame(Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length));
    }

    public static StationFrame ParseFrame(string json)
    {
        if (json.Length > MaximumDecodedLength) throw new InvalidDataException("观测快照过大");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        var time = DateTimeOffset.FromUnixTimeMilliseconds(root.GetProperty("ts").GetInt64());
        var source = root.GetProperty("source").GetString() ?? throw new JsonException("缺少观测节点");
        var stations = root.GetProperty("stations");
        if (stations.ValueKind != JsonValueKind.Object || source.Length > 100) throw new JsonException("观测结构错误");
        var readings = new Dictionary<string, StationReading>(StringComparer.Ordinal);
        foreach (var station in stations.EnumerateObject())
        {
            if (readings.Count >= 4096 || !ValidId(station.Name) || station.Value.ValueKind != JsonValueKind.Object)
                throw new JsonException("观测台站编号或数量异常");
            var value = station.Value;
            var intensity = value.GetProperty("i").GetDouble();
            if (!double.IsFinite(intensity) || intensity is < -3 or > 10) throw new JsonException("观测震度异常");
            double? pga = value.TryGetProperty("pga", out var acceleration) ? acceleration.GetDouble() : null;
            if (pga is { } number && (!double.IsFinite(number) || number is < 0 or > 100000)) throw new JsonException("PGA 异常");
            var triggered = value.TryGetProperty("alert", out var alert);
            if (triggered && alert.GetInt32() != 1) throw new JsonException("台站触发标记异常");
            // -3 is the documented no-signal sentinel; absence of pga is unknown, not zero.
            if (!readings.TryAdd(station.Name, new(station.Name, intensity == -3 ? null : intensity, pga, triggered)))
                throw new JsonException("快照含重复台站");
        }
        return new(time, source, readings.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private static bool ValidId(string id) => id.Length == 7 && id.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
}
