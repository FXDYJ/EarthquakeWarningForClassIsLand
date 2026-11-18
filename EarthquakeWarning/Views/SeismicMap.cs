using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EarthquakeWarning.Models;
using EarthquakeWarning.Services;
using System.Diagnostics;

namespace EarthquakeWarning.Views;

/// <summary>Offline geographic land outlines and clearly labelled estimated wavefronts.</summary>
public sealed class SeismicMap : Control
{
    private static readonly Lazy<IReadOnlyList<Point[]>> Land = new(LoadLand);
    private AlertSnapshot? _snapshot;
    private readonly Dictionary<string, Bitmap> _tiles = new();
    private OnlineMapTiles? _loader;
    private CancellationTokenSource? _tileCancellation;
    private string _tileKey = "";
    private string _template = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    private string _attribution = "© OpenStreetMap contributors";
    private bool _online = true;
    private int _zoomOffset;
    private double _panX;
    private double _panY;
    private Point? _drag;
    private double _dragX;
    private double _dragY;
    private string? _mapError;
    private DateTimeOffset _retryAfter;
    private string _legend = "";
    public event Action<string>? LegendChanged;

    public SeismicMap()
    {
        ClipToBounds = true;
        AttachedToVisualTree += (_, _) => { _loader = new OnlineMapTiles(); RefreshTiles(); };
        DetachedFromVisualTree += (_, _) =>
        {
            _tileCancellation?.Cancel();
            _loader?.Dispose();
            _loader = null;
            foreach (var bitmap in _tiles.Values) bitmap.Dispose();
            _tiles.Clear();
            _tileKey = "";
        };
        SizeChanged += (_, _) => RefreshTiles();
        PointerWheelChanged += (_, args) => { ZoomBy(args.Delta.Y > 0 ? 1 : -1); args.Handled = true; };
        PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _drag = args.GetPosition(this); _dragX = _panX; _dragY = _panY; args.Pointer.Capture(this);
        };
        PointerMoved += (_, args) =>
        {
            if (_drag is not { } start || _snapshot is null) return;
            var size = View(_snapshot).WorldSize;
            var delta = args.GetPosition(this) - start;
            _panX = _dragX - delta.X / size; _panY = _dragY - delta.Y / size;
            RefreshTiles(); InvalidateVisual();
        };
        PointerReleased += (_, args) => { _drag = null; args.Pointer.Capture(null); };
        PointerCaptureLost += (_, _) => _drag = null;
    }

    public void Configure(EarthquakeNotificationSettings settings)
    {
        var changed = _online != settings.EnableOnlineMap || _template != settings.MapTileTemplate;
        _online = settings.EnableOnlineMap;
        _template = settings.MapTileTemplate;
        _attribution = _template.Contains("tile.openstreetmap.org", StringComparison.OrdinalIgnoreCase)
            ? "© OpenStreetMap contributors" : settings.MapAttribution;
        if (changed)
        {
            _tileCancellation?.Cancel();
            foreach (var bitmap in _tiles.Values) bitmap.Dispose();
            _tiles.Clear(); _tileKey = "";
            RefreshTiles();
        }
    }

    public void Update(AlertSnapshot snapshot)
    {
        if (_snapshot?.EventKey != snapshot.EventKey) { _zoomOffset = 0; _panX = 0; _panY = 0; }
        _snapshot = snapshot;
        RefreshTiles();
        InvalidateVisual();
    }

    public void ZoomBy(int step) { _zoomOffset = Math.Clamp(_zoomOffset + step, -5, 10); RefreshTiles(); InvalidateVisual(); }
    public void Fit() { _zoomOffset = 0; _panX = 0; _panY = 0; RefreshTiles(); InvalidateVisual(); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_snapshot is not { } snapshot || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var report = snapshot.Report;
        var view = View(snapshot);
        Point Project(double lon, double lat)
        {
            var world = World(lon, lat);
            var dx = (world.X - view.X + 1.5) % 1 - 0.5;
            return new Point(Bounds.Width / 2 + dx * view.WorldSize, Bounds.Height / 2 + (world.Y - view.Y) * view.WorldSize);
        }
        using var clip = context.PushClip(new Rect(Bounds.Size));
        foreach (var ring in Land.Value)
        {
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                // Draw wrapped copies and let the drawing clip retain the local portion of large continents.
                var center = ring.Average(point => (point.X + 180) / 360);
                var shift = Math.Round(view.X - center);
                var projected = ring.Select(point =>
                {
                    var world = World(point.X, point.Y);
                    return new Point(Bounds.Width / 2 + (world.X + shift - view.X) * view.WorldSize,
                        Bounds.Height / 2 + (world.Y - view.Y) * view.WorldSize);
                }).ToArray();
                if (projected.Length == 0) continue;
                path.BeginFigure(projected[0], true);
                foreach (var point in projected.Skip(1)) path.LineTo(point);
                path.EndFigure(true);
            }
            context.DrawGeometry(Brush.Parse("#253448"), new Pen(Brush.Parse("#51647F"), 0.8), geometry);
        }
        foreach (var line in Boundaries.Value)
        {
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                if (line.Length == 0) continue;
                path.BeginFigure(Project(line[0].X, line[0].Y), false);
                foreach (var point in line.Skip(1)) path.LineTo(Project(point.X, point.Y));
                path.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(Brush.Parse("#53627B"), 0.7), geometry);
        }
        var loaded = 0;
        foreach (var tile in VisibleTiles(view))
        {
            if (!_online || !_tiles.TryGetValue(tile.Key, out var bitmap)) continue;
            context.DrawImage(bitmap, tile.Rectangle);
            loaded++;
        }
        if (loaded > 0) context.DrawRectangle(Brush.Parse("#16090C12"), null, new Rect(Bounds.Size));
        else DrawNames(context, view.Zoom, Project);
        var origin = Project(report.Longitude, report.Latitude);
        var user = Project(snapshot.UserLongitude, snapshot.UserLatitude);
        var elapsed = Math.Max(0, (DateTimeOffset.UtcNow - report.OriginTime).TotalSeconds);
        // Constant-velocity rings illustrate propagation only. The arrival timer uses the travel-time model.
        var kmPerPixel = 40075.017 * Math.Max(0.087, Math.Cos(report.Latitude * Math.PI / 180)) / view.WorldSize;
        var pRadius = elapsed * 6 / kmPerPixel;
        var sRadius = elapsed * 3.5 / kmPerPixel;
        context.DrawEllipse(Brush.Parse("#1736C9DF"), new Pen(Brush.Parse("#53D3E8"), 1.5), origin, pRadius, pRadius);
        context.DrawEllipse(Brush.Parse("#28E63E54"), new Pen(Brush.Parse("#FF6B79"), 2), origin, sRadius, sRadius);
        context.DrawLine(new Pen(Brush.Parse("#D2DCEB"), 1, dashStyle: DashStyle.Dash), origin, user);
        context.DrawEllipse(Brush.Parse("#FF485A"), new Pen(Brushes.White, 2), origin, 7, 7);
        context.DrawEllipse(Brush.Parse("#7CE9F5"), new Pen(Brushes.White, 2), user, 6, 6);
        MarkerLabel(context, "震中", origin + new Vector(12, -26), "#FFFFFF");
        MarkerLabel(context, "本地", user + new Vector(12, 8), "#C3FAFF");
        var mode = loaded > 0 ? $"在线 Z{view.Zoom} · {_attribution}"
            : $"离线 Z{view.Zoom} · Natural Earth" + (_online ? (_mapError is null ? " · 加载中" : " · 在线地图不可用") : "");
        var legend = $"P / S 波：传播示意，非实测  ·  {mode}";
        if (_legend != legend)
        {
            _legend = legend;
            Dispatcher.UIThread.Post(() => LegendChanged?.Invoke(legend));
        }
    }

    private MapView View(AlertSnapshot snapshot)
    {
        var quake = World(snapshot.Report.Longitude, snapshot.Report.Latitude);
        var user = World(snapshot.UserLongitude, snapshot.UserLatitude);
        var dx = (user.X - quake.X + 1.5) % 1 - 0.5;
        var scaleX = Math.Max(100, Bounds.Width - 120) / (256 * Math.Max(0.0003, Math.Abs(dx)));
        var scaleY = Math.Max(80, Bounds.Height - 140) / (256 * Math.Max(0.0003, Math.Abs(user.Y - quake.Y)));
        var zoom = Math.Clamp((int)Math.Floor(Math.Log2(Math.Min(scaleX, scaleY))) + _zoomOffset, 2, 18);
        return new MapView((quake.X + dx / 2 + _panX + 10) % 1, Math.Clamp((quake.Y + user.Y) / 2 + _panY, 0, 1), zoom);
    }

    private static Point World(double longitude, double latitude)
    {
        var radians = Math.Clamp(latitude, -85.05112878, 85.05112878) * Math.PI / 180;
        return new Point((longitude + 180) / 360, (1 - Math.Asinh(Math.Tan(radians)) / Math.PI) / 2);
    }

    private IEnumerable<MapTile> VisibleTiles(MapView view)
    {
        var count = 1 << view.Zoom;
        var left = (int)Math.Floor((view.X * view.WorldSize - Bounds.Width / 2) / 256);
        var top = (int)Math.Floor((view.Y * view.WorldSize - Bounds.Height / 2) / 256);
        for (var x = left; x <= left + (int)Math.Ceiling(Bounds.Width / 256); x++)
        for (var y = top; y <= top + (int)Math.Ceiling(Bounds.Height / 256); y++)
        {
            if (y < 0 || y >= count) continue;
            var wrapped = ((x % count) + count) % count;
            yield return new MapTile($"{view.Zoom}/{wrapped}/{y}", view.Zoom, wrapped, y,
                new Rect(x * 256 - view.X * view.WorldSize + Bounds.Width / 2,
                    y * 256 - view.Y * view.WorldSize + Bounds.Height / 2, 256, 256));
        }
    }

    private void RefreshTiles()
    {
        if (!_online || _loader is null || _snapshot is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var tiles = VisibleTiles(View(_snapshot)).ToArray();
        var key = _template + string.Join(",", tiles.Select(tile => tile.Key));
        if (key == _tileKey && (_mapError is null || DateTimeOffset.UtcNow < _retryAfter)) return;
        _tileKey = key;
        _mapError = null;
        _retryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
        _tileCancellation?.Cancel();
        _tileCancellation?.Dispose();
        _tileCancellation = new CancellationTokenSource();
        var token = _tileCancellation.Token;
        foreach (var tile in tiles.Where(tile => !_tiles.ContainsKey(tile.Key)).DistinctBy(tile => tile.Key)) _ = LoadTileAsync(_loader, tile, token);
        if (_tiles.Count > 80)
        {
            var visible = tiles.Select(tile => tile.Key).ToHashSet();
            foreach (var old in _tiles.Keys.Where(item => !visible.Contains(item)).ToArray()) { _tiles[old].Dispose(); _tiles.Remove(old); }
        }
    }

    private async Task LoadTileAsync(OnlineMapTiles loader, MapTile tile, CancellationToken token)
    {
        try
        {
            var bitmap = await loader.GetAsync(_template, tile.Zoom, tile.X, tile.Y, token).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested) { bitmap.Dispose(); return; }
                if (_tiles.Remove(tile.Key, out var old)) old.Dispose();
                _tiles[tile.Key] = bitmap;
                InvalidateVisual();
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            Debug.WriteLine($"地图瓦片加载失败：{error.Message}");
            Dispatcher.UIThread.Post(() => { if (!token.IsCancellationRequested) { _mapError = error.Message; InvalidateVisual(); } });
        }
    }

    private void DrawNames(DrawingContext context, int zoom, Func<double, double, Point> project)
    {
        var level = zoom < 4 ? 0 : zoom < 7 ? 1 : 2;
        var used = new List<Rect>();
        foreach (var label in Names.Value.Where(item => item.Level == level).OrderBy(item => item.Rank))
        {
            var point = project(label.Longitude, label.Latitude);
            var text = new FormattedText(label.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), level == 0 ? 20 : 15, Brush.Parse("#DCE8FA"));
            var rect = new Rect(point.X - text.Width / 2 - 3, point.Y - 11, text.Width + 6, 23);
            if (rect.Left < 5 || rect.Right > Bounds.Width - 5 || rect.Top < 5 || rect.Bottom > Bounds.Height - 5
                || used.Any(old => old.Intersects(rect.Inflate(7)))) continue;
            used.Add(rect);
            context.DrawRectangle(Brush.Parse("#C9182538"), null, rect, 3, 3);
            context.DrawText(text, new Point(rect.Left + 3, rect.Top));
        }
    }

    private static readonly Lazy<IReadOnlyList<AdministrativeName>> Names = new(() =>
    {
        using var stream = AssetLoader.Open(new Uri("avares://EarthquakeWarning/Assets/admin-labels.json"));
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Where(item => item.GetProperty("name").ValueKind == JsonValueKind.String)
            .Select(item => new AdministrativeName(item.GetProperty("name").GetString()!, item.GetProperty("lon").GetDouble(),
                item.GetProperty("lat").GetDouble(), item.GetProperty("level").GetInt32(), item.GetProperty("rank").GetInt32())).ToArray();
    });

    private sealed record AdministrativeName(string Name, double Longitude, double Latitude, int Level, int Rank);
    private sealed record MapTile(string Key, int Zoom, int X, int Y, Rect Rectangle);
    private sealed record MapView(double X, double Y, int Zoom) { public double WorldSize => 256 * (1 << Zoom); }

    private static void Label(DrawingContext context, string text, Point point, string color, double size = 16)
    {
        context.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), size, Brush.Parse(color)), point);
    }

    private static void MarkerLabel(DrawingContext context, string text, Point point, string color)
    {
        var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), 16, Brush.Parse(color));
        context.DrawRectangle(Brush.Parse("#EA101925"), null, new Rect(point.X - 4, point.Y - 2, label.Width + 8, label.Height + 4), 3, 3);
        context.DrawText(label, point);
    }

    private static IReadOnlyList<Point[]> LoadLand()
    {
        using var stream = AssetLoader.Open(new Uri("avares://EarthquakeWarning/Assets/land.geojson"));
        using var document = JsonDocument.Parse(stream);
        var rings = new List<Point[]>();
        foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
        {
            var geometry = feature.GetProperty("geometry");
            var coordinates = geometry.GetProperty("coordinates");
            var polygons = geometry.GetProperty("type").GetString() == "Polygon"
                ? new[] { coordinates } : coordinates.EnumerateArray().ToArray();
            foreach (var polygon in polygons)
                rings.Add(polygon[0].EnumerateArray().Select(pair => new Point(pair[0].GetDouble(), pair[1].GetDouble())).ToArray());
        }
        return rings;
    }

    private static readonly Lazy<IReadOnlyList<Point[]>> Boundaries = new(() =>
    {
        using var stream = AssetLoader.Open(new Uri("avares://EarthquakeWarning/Assets/provinces.geojson"));
        using var document = JsonDocument.Parse(stream);
        var lines = new List<Point[]>();
        foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
        {
            var geometry = feature.GetProperty("geometry");
            var coordinates = geometry.GetProperty("coordinates");
            var segments = geometry.GetProperty("type").GetString() == "LineString"
                ? new[] { coordinates } : coordinates.EnumerateArray().ToArray();
            foreach (var segment in segments)
                lines.Add(segment.EnumerateArray().Select(pair => new Point(pair[0].GetDouble(), pair[1].GetDouble())).ToArray());
        }
        return lines;
    });
}
