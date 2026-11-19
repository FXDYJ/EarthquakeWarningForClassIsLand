using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using EarthquakeWarning.Models;
using EarthquakeWarning.Services.Stations;

internal static class StationChecks
{
    private const string DirectoryCsv = "loc_code,id,lat,lon,floor,code,net,time,work\r\n1,125313C,23.3188,121.4564,2,962,3,2026-02-16,1\r\n,1268738,25.1144,121.5222,2,112,3,2026-02-18,0\r\n";
    private static string Frame(DateTimeOffset time, string readings = "\"125313C\":{\"i\":1.8,\"pga\":0.87,\"alert\":1}")
        => $"{{\"source\":\"test-core\",\"ts\":{time.ToUnixTimeMilliseconds()},\"stations\":{{{readings}}}}}";
    private static string Encode(string json)
    {
        using var data = new MemoryStream();
        using (var gzip = new GZipStream(data, CompressionMode.Compress, true)) gzip.Write(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(data.ToArray());
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("Station regression failed"); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or JsonException or FormatException or ArgumentOutOfRangeException) { return; }
        throw new Exception("Invalid station data accepted");
    }

    public static async Task RunAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var directory = TremStationParser.ParseDirectory("\uFEFF" + DirectoryCsv);
        Check(directory.Count == 2 && directory.Count(station => station.Active) == 1 && directory[0].Latitude == 23.3188);
        Reject(() => TremStationParser.ParseDirectory(DirectoryCsv.Replace("lat,", "bogus,")));
        Reject(() => TremStationParser.ParseDirectory(DirectoryCsv.Replace("23.3188", "NaN")));
        Reject(() => TremStationParser.ParseDirectory(DirectoryCsv.Replace("1268738", "125313C")));
        Console.WriteLine("PASS Station CSV validates headers coordinates IDs and activity");

        var frame = TremStationParser.DecodeFrame(Encode(Frame(now)));
        Check(frame.Readings["125313C"] is { Triggered: true, Intensity: 1.8, Pga: 0.87 });
        var noSignal = TremStationParser.ParseFrame(Frame(now, "\"125313C\":{\"i\":-3},\"1268738\":{\"i\":-0.2,\"pga\":0}"));
        Check(noSignal.Readings["125313C"].Intensity is null && noSignal.Readings["125313C"].Pga is null);
        Check(noSignal.Readings["1268738"].Intensity == -0.2 && noSignal.Readings["1268738"].Pga == 0);
        Console.WriteLine("PASS Station decode preserves unknown no-signal and measured zero distinctly");

        foreach (var (value, label) in new[] { (0.49, "0"), (0.5, "1"), (1.5, "2"), (4.5, "5弱"), (5.0, "5强"), (5.5, "6弱"), (6.0, "6强"), (6.5, "7") })
            Check(new StationReading("125313C", value, null, false).IntensityLabel == label);
        Console.WriteLine("PASS TREM scale boundaries retain lower upper subdivisions");

        Reject(() => TremStationParser.ParseFrame(Frame(now, "\"125313C\":{\"i\":99}")));
        Reject(() => TremStationParser.ParseFrame(Frame(now, "\"125313C\":{\"i\":1,\"alert\":0}")));
        Reject(() => TremStationParser.ParseFrame(Frame(now, "\"125313C\":{\"i\":1,\"pga\":-1}")));
        Reject(() => TremStationParser.ParseFrame(Frame(now, "\"125313C\":{\"i\":1},\"125313C\":{\"i\":2}")));
        Reject(() => TremStationParser.DecodeFrame(Encode(new string('a', TremStationParser.MaximumDecodedLength + 1))));
        Reject(() => TremStationParser.DecodeFrame(new string('a', TremStationParser.MaximumMessageLength + 1)));
        Console.WriteLine("PASS Invalid and oversized compressed station data are rejected");

        var store = new StationObservationStore();
        Check(!store.Apply(frame, now));
        store.Change(state => state with { Enabled = true, Directory = directory });
        Check(store.Apply(frame, now) && store.Snapshot.IsFresh(now));
        Check(!store.Apply(frame with { Time = now.AddMilliseconds(-1) }, now));
        Reject(() => store.Apply(frame with { Time = now.AddSeconds(6) }, now));
        Reject(() => store.Apply(frame with { Time = now.AddSeconds(-11) }, now));
        Check(store.Snapshot.Frame == frame);
        Console.WriteLine("PASS Disabled future stale and reordered frames cannot replace live observations");

        Check(store.Snapshot.CurrentReading("125313C", now.AddSeconds(6)) is null);
        store.Change(state => state with { Connected = false });
        Check(store.Snapshot.CurrentReading("125313C", now) is null);
        store.Apply(TremStationParser.ParseFrame(Frame(now.AddMilliseconds(1), "")), now);
        Check(store.Snapshot.Frame!.Readings.Count == 0 && store.Snapshot.CurrentReading("125313C", now) is null);
        Console.WriteLine("PASS Full replacement expiry and disconnect clear every stale reading");

        var messages = new List<SseMessage>();
        await foreach (var message in BoundedSseReader.ReadAsync(new StringReader(": heartbeat\r\nevent: info\r\ndata: {}\r\n\r\nevent: trem.rts.v1\ndata: line1\ndata: line2\n\ndata: incomplete"), CancellationToken.None)) messages.Add(message);
        Check(messages.Count == 2 && messages[0].Event == "info" && messages[1] == new SseMessage("trem.rts.v1", "line1\nline2"));
        try
        {
            await foreach (var message in BoundedSseReader.ReadAsync(new StringReader(new string('x', TremStationParser.MaximumMessageLength + 1)), CancellationToken.None)) { }
            throw new Exception("Oversized SSE accepted");
        }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS Bounded SSE handles CRLF comments multiline data and incomplete EOF");

        using var cancellation = new CancellationTokenSource();
        using var service = new TremStationService(new FakeHandler());
        var run = service.RunAsync(cancellation.Token);
        for (var attempt = 0; attempt < 100 && service.Snapshot.Frame is null; attempt++) await Task.Delay(10);
        Check(service.Snapshot.Frame is not null && service.Snapshot.Directory.Count == 2);
        cancellation.Cancel(); await run;
        Check(!service.Snapshot.Enabled && !service.Snapshot.Connected && service.Snapshot.Frame is null);
        Console.WriteLine("PASS HTTP directory and RTS lifecycle cancel cleanly without alert infrastructure");
        Console.WriteLine("8 station regression groups passed.");
    }

    public static async Task LiveAsync()
    {
        using var token = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var service = new TremStationService();
        var run = service.RunAsync(token.Token);
        try
        {
            while (service.Snapshot.Directory.Count == 0 || !service.Snapshot.IsFresh(DateTimeOffset.UtcNow))
            {
                token.Token.ThrowIfCancellationRequested();
                await Task.Delay(100, token.Token);
            }
            var state = service.Snapshot;
            Console.WriteLine($"PASS live TREM: {state.Directory.Count} directory devices, {state.Directory.Count(s => s.Active)} active, {state.VisibleCount(DateTimeOffset.UtcNow)} matched observations; source={state.Frame!.Source}");
            Console.WriteLine($"Sample time difference (not a latency guarantee): {(DateTimeOffset.UtcNow-state.Frame.Time).TotalSeconds:F3}s");
        }
        finally { token.Cancel(); await run; }
        Check(!service.Snapshot.Enabled && service.Snapshot.Frame is null);
        Console.WriteLine("PASS live cancellation clears readings");
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath == "/resource/station")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(DirectoryCsv, Encoding.UTF8, "application/json") });
            var content = new StreamContent(new BlockingStream(Encoding.UTF8.GetBytes($"data: {Encode(Frame(DateTimeOffset.UtcNow))}\n\n")));
            content.Headers.ContentType = new("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
    private sealed class BlockingStream(byte[] initial) : Stream
    {
        private int _offset;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (_offset == initial.Length) { await Task.Delay(Timeout.Infinite, token); return 0; }
            var length = Math.Min(buffer.Length, initial.Length - _offset);
            initial.AsMemory(_offset, length).CopyTo(buffer); _offset += length; return length;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException(); public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
