using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using EarthquakeWarning.Models;

namespace EarthquakeWarning.Services.Stations;

/// <summary>One shared public RTS connection, plus a daily station directory refresh.</summary>
public sealed class TremStationService : IDisposable
{
    private static readonly string[] Directories = ["https://static.core-tnn1.exptech.dev/resource/station", "https://static.core-tyo1.exptech.dev/resource/station"];
    private static readonly string[] Streams = ["https://api.lb.exptech.dev/api/v1/trem/sse/rts?mode=live", "https://api.lb-tpe1.exptech.dev/api/v1/trem/sse/rts?mode=live", "https://api.lb-khh1.exptech.dev/api/v1/trem/sse/rts?mode=live"];
    private readonly HttpClient _http;
    public StationObservationStore Store { get; } = new();
    public StationNetworkState Snapshot => Store.Snapshot;

    public TremStationService(HttpMessageHandler? handler = null)
    {
        _http = new(handler ?? new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
        { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ClassIsland-EarthquakeWarning/0.12.0 (+https://github.com/FXDYJ/EarthquakeWarningForClassIsLand)");
    }

    public async Task RunAsync(CancellationToken token)
    {
        Store.Change(state => state with { Enabled = true, Connected = false, Frame = null, Status = "正在连接 TREM 台站观测…" });
        try { await Task.WhenAll(RefreshDirectoryAsync(token), ListenAsync(token)).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { Store.Change(state => state with { Enabled = false, Connected = false, Frame = null, Status = "台站观测已暂停" }); }
    }

    private async Task RefreshDirectoryAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var success = false;
            foreach (var address in Directories)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    using var response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                    using var data = new MemoryStream();
                    var buffer = new byte[8192]; int length;
                    while ((length = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                    {
                        if (data.Length + length > TremStationParser.MaximumMessageLength) throw new InvalidDataException("台站目录响应过大");
                        data.Write(buffer, 0, length);
                    }
                    // The provider deliberately labels this CSV response as application/json.
                    var directory = TremStationParser.ParseDirectory(Encoding.UTF8.GetString(data.GetBuffer(), 0, (int)data.Length));
                    token.ThrowIfCancellationRequested();
                    Store.Change(state => state with { Directory = directory, DirectoryError = "", DirectoryUpdated = DateTimeOffset.UtcNow });
                    success = true; break;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception error)
                {
                    Debug.WriteLine($"[TREM 目录] {address}: {error}");
                    Store.Change(state => state with { DirectoryError = $"台站目录读取失败，将重试：{error.Message}" });
                }
            }
            await Task.Delay(success ? TimeSpan.FromDays(1) : TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
        }
    }

    private async Task ListenAsync(CancellationToken token)
    {
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
                connection.CancelAfter(TimeSpan.FromSeconds(15));
                using var request = new HttpRequestMessage(HttpMethod.Get, Streams[failures % Streams.Length]);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connection.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentType?.MediaType != "text/event-stream") throw new InvalidDataException("台站接口未返回实时流");
                await using var stream = await response.Content.ReadAsStreamAsync(connection.Token).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                Store.Change(state => state with { Connected = true, Status = "已连接，等待观测快照" });
                await foreach (var message in BoundedSseReader.ReadAsync(reader, connection.Token).ConfigureAwait(false))
                {
                    if (message.Event is "message" or "trem.rts.v1")
                    {
                        var frame = TremStationParser.DecodeFrame(message.Data);
                        token.ThrowIfCancellationRequested();
                        if (Store.Apply(frame, DateTimeOffset.UtcNow)) failures = 0;
                    }
                    // Live mode produces snapshots twice a second; a silent connection is re-established.
                    connection.CancelAfter(TimeSpan.FromSeconds(15));
                }
                throw new IOException("台站观测流已结束");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                Debug.WriteLine($"[TREM 观测流] {error}");
                Store.Change(state => state with { Connected = false, Status = $"台站连接中断，将重试：{(error is OperationCanceledException ? "读取超时" : error.Message)}" });
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 3 * Math.Pow(2, Math.Min(failures++, 4)))), token).ConfigureAwait(false);
        }
    }

    public void Dispose() => _http.Dispose();
}
