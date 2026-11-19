using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EarthquakeWarning.Models;

public partial class StationMonitorViewModel : ObservableObject
{
    [ObservableProperty] private string _status = "等待台站观测";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private string _directoryStatus = "";
    [ObservableProperty] private bool _isSimulation;
    [ObservableProperty] private bool _hasNoStations = true;
    [ObservableProperty] private string _emptyMessage = "等待台站目录，获取后显示观测值";
    [ObservableProperty] private IReadOnlyList<StationObservationRow> _rows = Array.Empty<StationObservationRow>();
    public string Attribution => StationNetworkState.Attribution;

    public void Update(StationNetworkState state, DateTimeOffset now)
    {
        Status = state.Summary(now);
        IsSimulation = state.Simulated;
        HasNoStations = !state.Directory.Any(station => station.Active);
        EmptyMessage = !state.Enabled ? "台站观测已暂停\n在提醒设置中开启后获取数据"
            : state.DirectoryError.Length > 0 ? "台站目录暂不可用\n自动重试后恢复位置与观测列表" : "等待台站目录\n获取后显示位置与观测值";
        Detail = state.Frame is { } frame
            ? $"节点 {frame.Source} · 最近快照 {frame.Time.ToOffset(TimeSpan.FromHours(8)):MM-dd HH:mm:ss.fff} · {(state.Directory.Count == 0 ? "目录未就绪，暂不提供台站读数列表" : state.IsFresh(now) ? "当前可用" : "读数不可用，已隐藏")}"
            : "尚无观测快照；不会触发预警、语音或系统音量接管";
        DirectoryStatus = state.DirectoryError.Length > 0 ? state.DirectoryError
            : state.DirectoryUpdated is { } time ? $"目录更新 {time.ToOffset(TimeSpan.FromHours(8)):MM-dd HH:mm} · {state.Directory.Count} 个设备记录" : "正在等待台站目录；无坐标时不会绘制站点";
        Rows = state.Directory.Where(station => station.Active).Select(station =>
        {
            var reading = state.CurrentReading(station.Id, now);
            return new StationObservationRow(station.Id, $"{station.Latitude:F4}°N / {station.Longitude:F4}°E · {station.Floor} 层",
                reading?.IntensityLabel ?? "—", reading?.PgaLabel ?? "—",
                reading is null ? "暂无有效数据" : reading.Intensity is null ? "无有效震度" : reading.Triggered ? "来源检测中" : "有观测",
                Brush.Parse(StationPalette.Color(reading)));
        }).ToArray();
    }
}

public sealed record StationObservationRow(string Id, string Location, string Intensity, string Pga, string Status, IBrush Color);

public static class StationPalette
{
    public static string Color(StationReading? reading) => reading?.Intensity switch
    {
        null => "#9EABC1", < 0.5 => "#8AB5CC", < 1.5 => "#65CBF3", < 2.5 => "#7ED9B1",
        < 3.5 => "#FFE08A", < 4.5 => "#FFBD66", < 5.5 => "#FF8C68", < 6.5 => "#FF6B79", _ => "#F2B0EA"
    };
}
