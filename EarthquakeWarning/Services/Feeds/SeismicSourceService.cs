using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EarthquakeWarning.Models;

namespace EarthquakeWarning.Services.Feeds;

public sealed class SeismicSourceService : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
    { Timeout = TimeSpan.FromSeconds(8) };
    private readonly ConcurrentDictionary<string, FeedHealth> _health = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<SeismicReport>> _latest = new();
    public ConsensusEngine Engine { get; } = new();
    public IReadOnlyList<FeedHealth> Health => _health.Values.OrderBy(value => value.Name).ToArray();
    public IReadOnlyList<SeismicReport> LatestReports => _latest.Values.SelectMany(value => value)
        .OrderByDescending(value => value.OriginTime).ThenByDescending(value => value.ReportNumber).Take(50).ToArray();
    private static readonly string[] WolfxFeeds =
        { "cenc_eew", "sc_eew", "fj_eew", "cq_eew", "jma_eew", "cenc_eqlist", "jma_eqlist" };

    public Task RunAsync(string mode, CancellationToken token)
    {
        _health.Clear();
        _latest.Clear();
        var jobs = new List<Task>();
        foreach (var feed in WolfxFeeds.Concat(new[] { "miui", "cenc-early", "cenc-bulletin" }))
        {
            if (mode != "combined" && mode != feed && !(mode == "wolfx-live" && feed.EndsWith("eew", StringComparison.Ordinal))) continue;
            jobs.Add(PollAsync(feed, token));
        }
        if (mode is "combined" or "wolfx-live") jobs.Add(ListenAsync(token));
        return Task.WhenAll(jobs);
    }

    private async Task PollAsync(string feed, CancellationToken token)
    {
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var request = BuildRequest(feed);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(token);
                Receive(feed, json);
                _health[feed] = new(feed, DateTimeOffset.Now, "接口可达", true);
                failures = 0;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                failures++;
                Failed(feed, error);
            }
            var seconds = feed switch { "cenc-early" => 3, "miui" => 5,
                "cenc-bulletin" => 15, "cenc_eqlist" or "jma_eqlist" => 30, _ => 10 };
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, seconds * Math.Pow(2, Math.Min(failures, 3)))), token);
        }
    }

    private static HttpRequestMessage BuildRequest(string feed)
    {
        if (feed == "miui")
        {
            var parameters = new Dictionary<string, string> { ["version"] = "2" };
            parameters["sign"] = MiuiEarthquakeSigning.MakeSignature(parameters);
            return new HttpRequestMessage(HttpMethod.Post, "https://srv.sec.miui.com/earthquake/warning/records")
            { Content = new FormUrlEncodedContent(parameters) };
        }
        if (feed.StartsWith("cenc-", StringComparison.Ordinal))
        {
            var alarm = feed == "cenc-early" ? 2 : 1;
            return new HttpRequestMessage(HttpMethod.Get,
                $"https://dzyj.cenc.ac.cn/api/earthquake/event/cdn/v1/list?app_id=dkcxbftqof0h&alarm_type={alarm}&page_no=1&page_size=10&v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        }
        return new HttpRequestMessage(HttpMethod.Get, $"https://api.wolfx.jp/{feed}.json");
    }

    private async Task ListenAsync(CancellationToken token)
    {
        var retry = 0;
        while (!token.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            try
            {
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(token);
                connect.CancelAfter(TimeSpan.FromSeconds(10));
                await socket.ConnectAsync(new Uri("wss://ws-api.wolfx.jp/all_eew"), connect.Token);
                retry = 0;
                var buffer = new byte[8192];
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        using var receive = CancellationTokenSource.CreateLinkedTokenSource(token);
                        receive.CancelAfter(TimeSpan.FromSeconds(90));
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), receive.Token);
                        if (result.MessageType == WebSocketMessageType.Close) throw new IOException("服务端关闭连接");
                        if (message.Length + result.Count > 1024 * 1024) throw new InvalidDataException("推送报文过大");
                        message.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                    if (result.MessageType != WebSocketMessageType.Text) continue;
                    var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    try
                    {
                        using var document = JsonDocument.Parse(json);
                        if (document.RootElement.TryGetProperty("type", out var type) && type.GetString() == "heartbeat")
                            await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("ping")), WebSocketMessageType.Text, true, token);
                        else Receive("wolfx-live", json);
                        _health["wolfx-live"] = new("Wolfx WebSocket", DateTimeOffset.Now, "实时连接正常", true);
                    }
                    catch (JsonException error) { Failed("wolfx-live", error); }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) { Failed("wolfx-live", error); }
            finally { socket.Abort(); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(retry++, 5)))), token);
        }
    }

    private void Receive(string feed, string json)
    {
        var now = DateTimeOffset.UtcNow;
        var reports = SeismicParser.Parse(feed, json);
        foreach (var group in reports.Where(report => report.HasValidParameters && !report.Training).GroupBy(report => report.Feed))
            _latest[group.Key] = group.GroupBy(report => report.EventId)
                .Select(items => items.OrderByDescending(item => item.ReportNumber).ThenByDescending(item => item.ReportTime).First())
                .OrderByDescending(item => item.OriginTime).Take(10).ToArray();
        foreach (var report in reports.OrderBy(item => item.ReportTime).ThenBy(item => item.ReportNumber))
            Engine.Add(report, now);
    }

    private void Failed(string feed, Exception error)
    {
        Debug.WriteLine($"[地震预警/{feed}] {error}");
        _health.TryGetValue(feed, out var previous);
        _health[feed] = new(feed, previous?.LastSuccess, $"重试中：{error.Message}", false);
    }

    public void Dispose() => _http.Dispose();
}
