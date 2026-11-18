using EarthquakeWarning.Models;
using EarthquakeWarning.Services.Feeds;
using EarthquakeWarning.Calculators;
using System.Text.Json;

var now = DateTimeOffset.UtcNow;
var first = new SeismicReport("cenc_eew", "Wolfx", "中国地震台网", "event-a", 1, now.AddSeconds(-5), now,
    35, 118, 5.8, null, "模拟震中");
var cases = new (string Name, Action Test)[]
{
    ("A single gateway stays pending", () =>
    {
        var engine = Build(first); Check(!One(engine).Confirmed);
        engine.Add(first with { Feed = "sc_eew" }, now);
        Check(One(engine).MatchingGateways == 1 && !One(engine).Confirmed);
    }),
    ("Local intensity selects three distinct sound/visual profiles", () =>
    {
        Check(AlertSeverity.FromIntensity(2.99).Beeps == 0);
        Check(AlertSeverity.FromIntensity(3).Beeps == 1);
        Check(AlertSeverity.FromIntensity(5.99).Beeps == 1);
        Check(AlertSeverity.FromIntensity(6).Beeps == 2);
        Check(AlertSeverity.FromIntensity(3).Accent != AlertSeverity.FromIntensity(6).Accent);
    }),
    ("Two matching delivery services confirm one event", () =>
    {
        var engine = Build(first, first with { Gateway = "CENC", Feed = "cenc-early", EventId = "other-format" });
        Check(One(engine).Confirmed && One(engine).Reports.Count == 2);
    }),
    ("A conflicting magnitude holds the alarm", () =>
    {
        var engine = Build(first, first with { Gateway = "MIUI", Magnitude = 8 });
        Check(!One(engine).Confirmed && One(engine).HasConflict);
        engine.Add(first with { Gateway = "MIUI", ReportNumber = 2, Magnitude = 6 }, now);
        Check(One(engine).Confirmed);
    }),
    ("Old report replay cannot regress the latest sequence", () =>
    {
        var engine = Build(first with { ReportNumber = 3 });
        engine.Add(first with { ReportNumber = 2, Magnitude = 9 }, now);
        Check(One(engine).Preferred.Magnitude == first.Magnitude);
    }),
    ("Three gateway agreement must be pairwise, not only match a middle value", () =>
    {
        var engine = Build(first with { Magnitude = 4.8 }, first with { Gateway = "MIUI", Magnitude = 5.8 },
            first with { Gateway = "CENC", Magnitude = 6.8 });
        Check(One(engine).HasConflict && !One(engine).Confirmed);
    }),
    ("Older cancellations cannot revoke a newer report", () =>
    {
        var engine = Build(first with { ReportNumber = 3 });
        engine.Add(first with { ReportNumber = 2, Cancelled = true }, now);
        Check(!One(engine).Cancelled);
    }),
    ("Cancellation is terminal for that event", () =>
    {
        var engine = Build(first, first with { Gateway = "MIUI" });
        engine.Add(first with { ReportNumber = 2, Cancelled = true }, now);
        engine.Add(first with { ReportNumber = 1 }, now);
        Check(One(engine).Cancelled && !One(engine).Confirmed);
    }),
    ("Stale, future, invalid and training reports are filtered", () =>
    {
        Check(Build(first with { OriginTime = now.AddMinutes(-6) }).GetEvents(now, "combined").Count == 0);
        Check(Build(first with { OriginTime = now.AddMinutes(1) }).GetEvents(now, "combined").Count == 0);
        Check(Build(first with { Training = true }).GetEvents(now, "combined").Count == 0);
        Check(Build(first with { Magnitude = double.NaN }).GetEvents(now, "combined").Count == 0);
    }),
    ("Two catalog entries alone cannot trigger an early warning", () =>
    {
        var engine = Build(first with { Kind = ReportKind.Bulletin }, first with { Gateway = "CENC", Kind = ReportKind.Bulletin });
        Check(!One(engine).Confirmed);
    }),
    ("Nearby separate IDs on one feed stay separate", () =>
    {
        var engine = Build(first, first with { EventId = "aftershock", OriginTime = first.OriginTime.AddSeconds(3) });
        Check(engine.GetEvents(now, "combined").Count == 2);
    }),
    ("Single-source selection cannot borrow other gateway votes", () =>
    {
        var engine = Build(first, first with { Gateway = "MIUI", Feed = "miui" });
        Check(!engine.GetEvents(now, "miui").Single().Confirmed);
    }),
    ("CENC preserves sequence, stable third-party ID and unknown depth", () =>
    {
        var json = "{\"code\":0,\"data\":{\"spot_infos\":[{\"id\":\"report-id\",\"third_id\":\"stable-event\",\"serial_number\":3,\"created_at\":1794586672,\"latitude\":42.221,\"longitude\":83.663,\"level\":\"5.2\"}]}}";
        var report = SeismicParser.Parse("cenc-early", json).Single();
        Check(report.EventId == "stable-event" && report.ReportNumber == 3 && report.Depth is null && report.Kind == ReportKind.EarlyWarning);
    }),
    ("MIUI zero depth is unknown and timestamps retain milliseconds", () =>
    {
        var json = "{\"code\":0,\"data\":[{\"eventId\":202611140017,\"update\":3,\"startAt\":1794586672500,\"updateAt\":1794586716000,\"latitude\":42.221,\"longitude\":83.663,\"magnitude\":5.2,\"depth\":0}]}";
        var report = SeismicParser.Parse("miui", json).Single();
        Check(report.Depth is null && report.OriginTime.Millisecond == 500 && report.ReportNumber == 3);
    }),
    ("JMA local time, training and cancellation survive normalization", () =>
    {
        var json = "{\"type\":\"jma_eew\",\"EventID\":\"test\",\"OriginTime\":\"2026/11/17 19:32:58\",\"AnnouncedTime\":\"2026/11/17 19:33:46\",\"Serial\":6,\"Magnitude\":4.7,\"Latitude\":40.6,\"Longitude\":143.8,\"isTraining\":true,\"isCancel\":true}";
        var report = SeismicParser.Parse("wolfx-live", json).Single();
        Check(report.OriginTime.Offset == TimeSpan.FromHours(9) && report.Training && report.Cancelled);
    }),
    ("Malformed business responses fail visibly", () =>
    {
        try { SeismicParser.Parse("miui", "{\"code\":995,\"desc\":\"signature error\"}"); throw new Exception("Business error swallowed"); }
        catch (JsonException) { }
    }),
    ("Arrival is anchored to the event time, including past arrival", () =>
    {
        var verified = One(Build(first, first with { Gateway = "CENC" }));
        var snapshot = new AlertSnapshot(verified, 35, 118);
        Check(snapshot.Remaining < 5 && double.IsFinite(snapshot.LocalIntensity));
        Check(double.IsFinite(HuaniaEarthQuakeCalculator.GetCountDownSeconds(10, 0)));
    })
};
foreach (var test in cases) { test.Test(); Console.WriteLine($"PASS {test.Name}"); }
Console.WriteLine($"{cases.Length} regression checks passed.");

ConsensusEngine Build(params SeismicReport[] reports)
{
    var engine = new ConsensusEngine();
    foreach (var report in reports) engine.Add(report, now);
    return engine;
}
VerifiedEarthquake One(ConsensusEngine engine) => engine.GetEvents(now, "combined").Single();
void Check(bool condition) { if (!condition) throw new Exception("Regression check failed"); }
