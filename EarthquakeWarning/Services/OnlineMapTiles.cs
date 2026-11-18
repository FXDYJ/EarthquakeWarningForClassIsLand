using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;

namespace EarthquakeWarning.Services;

/// <summary>Requests only visible tiles, with an identifying agent and a seven-day disk cache.</summary>
public sealed class OnlineMapTiles : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly SemaphoreSlim _concurrency = new(3);
    private readonly string _cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassIsland", "EarthquakeWarning", "MapTiles");

    public OnlineMapTiles()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ClassIsland-EarthquakeWarning/0.10 (+https://github.com/FXDYJ/EarthquakeWarningForClassIsLand)");
    }

    public async Task<Bitmap> GetAsync(string template, int zoom, int x, int y, CancellationToken token)
    {
        var url = template.Replace("{z}", zoom.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidOperationException("地图模板须为 HTTPS，包含 {z}/{x}/{y}");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        token = timeout.Token;
        var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".png";
        var path = Path.Combine(_cache, fileName);
        await _concurrency.WaitAsync(token);
        try
        {
            if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromDays(7))
            {
                var cached = await File.ReadAllBytesAsync(path, token);
                try { using var decoded = new MemoryStream(cached); return new Bitmap(decoded); }
                catch (Exception error) when (error is not OperationCanceledException) { System.Diagnostics.Debug.WriteLine($"地图缓存解码失败：{error.Message}"); File.Delete(path); }
            }
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new InvalidDataException("地图瓦片过大");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var content = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0)
            {
                if (content.Length + read > 2 * 1024 * 1024) throw new InvalidDataException("地图瓦片过大");
                content.Write(buffer, 0, read);
            }
            var bytes = content.ToArray();
            using var decodedTile = new MemoryStream(bytes);
            var bitmap = new Bitmap(decodedTile);
            try
            {
                if (response.Headers.CacheControl?.NoStore == true) return bitmap;
                Directory.CreateDirectory(_cache);
                var files = new DirectoryInfo(_cache).GetFiles("*.png");
                foreach (var old in files.OrderBy(file => file.LastWriteTimeUtc).Take(Math.Max(0, files.Length - 240))) old.Delete();
                await File.WriteAllBytesAsync(path, bytes, token);
                var maxAge = response.Headers.CacheControl?.MaxAge;
                if (maxAge > TimeSpan.FromDays(7)) File.SetLastWriteTimeUtc(path, DateTime.UtcNow + maxAge.Value - TimeSpan.FromDays(7));
            }
            catch (IOException) { /* A readable tile can still be used when the cache is temporarily locked. */ }
            catch (UnauthorizedAccessException) { /* The in-memory tile remains available. */ }
            catch (OperationCanceledException) { bitmap.Dispose(); throw; }
            return bitmap;
        }
        finally { _concurrency.Release(); }
    }

    public void Dispose() => _http.Dispose();
}
