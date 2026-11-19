using EarthquakeWarning.Models;
using EarthquakeWarning.Services.Feeds;
using EarthquakeWarning.Calculators;
using System.Text.Json;

if (args.Contains("--stations-live")) { await StationChecks.LiveAsync(); return; }

var now = DateTimeOffset.UtcNow;
var first = new SeismicReport("cenc_eew", "Wolfx", "中国地震台网", "event-a", 1, now.AddSeconds(-5), now,
    35, 118, 5.8, 10, "山东测试震中");
var official = first with { Gateway = "CENC", Feed = "cenc-bulletin", EventId = "formal-a", Kind = ReportKind.Bulletin };
var cases = new (string Name, Action Test)[]
{
    ("Single gateway and Wolfx relay duplicates stay pending", () =>
    {
        var e = Build(first, first with { Feed = "sc_eew" });
        Check(!One(e).Confirmed && One(e).MatchingGateways == 1);
    }),
    ("Two or three EEW relays alone cannot confirm", () =>
    {
        Check(!One(Build(first, first with { Gateway = "MIUI" }, first with { Gateway = "CENC", Feed = "cenc-early" })).Confirmed);
    }),
    ("Only precise official CENC bulletin plus another gateway EEW confirms", () =>
    {
        Check(One(Build(first, official)).Confirmed);
        Check(One(Build(first, official)).Preferred == official);
        Check(!One(Build(first, official with { Feed = "cenc-early" })).Confirmed);
        Check(!One(Build(first, official with { Gateway = "MIUI" })).Confirmed);
        Check(!One(Build(first, official with { Kind = ReportKind.EarlyWarning })).Confirmed);
    }),
    ("Two bulletins cannot trigger early warning", () =>
        Check(!One(Build(first with { Kind = ReportKind.Bulletin }, official)).Confirmed)),
    ("All active evidence must agree within strict magnitude tolerance", () =>
    {
        Check(!One(Build(first with { Magnitude = 6.2 }, official)).Confirmed);
        Check(One(Build(first with { Magnitude = 6.1 }, official)).Confirmed);
        var e = Build(first with { Magnitude = 5.5 }, official, first with { Gateway = "MIUI", Magnitude = 6.1 });
        Check(One(e).HasConflict && !One(e).Confirmed);
    }),
    ("Strict time position and depth differences veto confirmation", () =>
    {
        Check(!One(Build(first with { OriginTime = first.OriginTime.AddSeconds(-6) }, official)).Confirmed);
        Check(!One(Build(first with { Latitude = 35.25 }, official)).Confirmed);
        Check(!One(Build(first with { Depth = 26 }, official)).Confirmed);
    }),
    ("Unknown or deep official depth never upgrades", () =>
    {
        Check(!One(Build(first, official with { Depth = null })).Confirmed);
        Check(!One(Build(first, official with { Depth = 31 })).Confirmed);
    }),
    ("Old sequence cannot overwrite a latest report", () =>
    {
        var e = Build(first with { ReportNumber = 3 }, official);
        e.Add(first with { ReportNumber = 2, Magnitude = 9 }, now);
        Check(One(e).Confirmed && One(e).Reports.Single(r => r.Gateway == "Wolfx").Magnitude == 5.8);
    }),
    ("Same-revision rewrite and invalid newer parameters fail closed", () =>
    {
        var e = Build(first, official);
        e.Add(first with { Magnitude = 5.9 }, now);
        Check(!One(e).Confirmed);
        e = Build(first, official);
        e.Add(first with { ReportNumber = 2, Magnitude = double.NaN }, now);
        Check(!One(e).Confirmed);
    }),
    ("Same numbered revision cannot rewrite parameters with a newer timestamp", () =>
    {
        var e = Build(first, official);
        e.Add(first with { Magnitude = 5.9, ReportTime = now.AddSeconds(1) }, now.AddSeconds(1));
        Check(!One(e).Confirmed);
    }),
    ("Sparse cancellation revokes the current event", () =>
    {
        var e = Build(first, official);
        var cancel = SeismicParser.Parse("cenc_eew", "{\"EventID\":\"event-a\",\"isCancel\":1}").Single();
        e.Add(cancel, now);
        Check(One(e).Cancelled && !One(e).Confirmed);
    }),
    ("Regressed cancellation still vetoes a newer affirmative revision", () =>
    {
        var e = Build(first with { ReportNumber = 3 }, official);
        e.Add(first with { ReportNumber = 1, Cancelled = true }, now);
        Check(One(e).Cancelled && !One(e).Confirmed);
    }),
    ("Cancel-before-first-report cannot be resurrected by replay", () =>
    {
        var e = Build(first with { Cancelled = true });
        e.AddBatch(new[] { official, first }, now);
        Check(!One(e).Confirmed && One(e).Cancelled);
    }),
    ("Unresolved sparse cancellation holds unrelated escalation conservatively", () =>
    {
        var e = Build(first with { EventId = "unknown", OriginTime = DateTimeOffset.MinValue, Cancelled = true });
        e.AddBatch(new[] { first, official }, now);
        Check(!One(e).Confirmed);
    }),
    ("Latest response applied atomically without historical affirmative vote", () =>
    {
        var e = Build(official);
        e.AddBatch(new[] { first, first with { ReportNumber = 2, Magnitude = 8 } }, now);
        Check(!One(e).Confirmed);
        e = Build(official);
        e.AddBatch(new[] { first, first with { Cancelled = true } }, now);
        Check(!One(e).Confirmed);
    }),
    ("Source reset clears votes but retains stable IDs and cancellation", () =>
    {
        var e = Build(first, official);
        var id = One(e).Id;
        e.ResetVotes();
        Check(e.GetEvents(now, "combined").Count == 0);
        e.AddBatch(new[] { first, official }, now);
        Check(One(e).Id == id && One(e).Confirmed);
        e.Add(first with { Cancelled = true }, now);
        e.ResetVotes();
        e.AddBatch(new[] { official, first }, now);
        Check(!One(e).Confirmed);
    }),
    ("Reset preserves ambiguous revisions and sequence high-water marks", () =>
    {
        var e = Build(first, official);
        e.Add(first with { Magnitude = 5.9 }, now);
        e.ResetVotes(); e.AddBatch(new[] { first, official }, now);
        Check(!One(e).Confirmed);
        e = Build(first with { ReportNumber = 3 }, official);
        e.ResetVotes(); e.AddBatch(new[] { first, official }, now);
        Check(!One(e).Confirmed);
    }),
    ("Evidence expires unless current responses refresh it", () =>
    {
        var e = Build(first, official);
        Check(!e.GetEvents(now.AddSeconds(31), "combined").Single().Confirmed);
        e.AddBatch(new[] { first, official }, now.AddSeconds(31));
        Check(e.GetEvents(now.AddSeconds(31), "combined").Single().Confirmed);
    }),
    ("Stale future invalid training and future report timestamps rejected", () =>
    {
        foreach (var r in new[] { first with { OriginTime = now.AddMinutes(-6) }, first with { OriginTime = now.AddSeconds(1) },
            first with { Training = true }, first with { Magnitude = double.NaN }, first with { ReportTime = now.AddMinutes(1) } })
            Check(Build(r).GetEvents(now, "combined").Count == 0);
    }),
    ("Separate nearby IDs on one feed cannot be merged", () =>
    {
        var e = Build(first, first with { EventId = "b", OriginTime = first.OriginTime.AddSeconds(-3) });
        Check(e.GetEvents(now, "combined").Count == 2);
        e.Add(official, now);
        Check(e.GetEvents(now, "combined").All(x => !x.Confirmed));
    }),
    ("Single source selection cannot borrow votes", () => Check(!Build(first, official).GetEvents(now, "cenc_eew").Single().Confirmed)),
    ("China eastern intensity uses epicentral distance and base-10 logarithm", () =>
    {
        Check(Math.Abs(HuaniaEarthQuakeCalculator.GetIntensity(6, 100, 10, 118) - 4.84783556749667) < 1e-10);
        Check(HuaniaEarthQuakeCalculator.GetIntensity(6, 100, 10, 118) == HuaniaEarthQuakeCalculator.GetIntensity(6, 100, 30, 118));
        Check(Math.Abs(HuaniaEarthQuakeCalculator.GetIntensity(6, 100, 10, 100) - 4.658845468866187) < 1e-10);
    }),
    ("Intensity increases with magnitude and decreases with epicentral distance", () =>
    {
        foreach (var longitude in new[] { 100d, 118d })
        {
            for (var r = 0; r < 500; r++) Check(HuaniaEarthQuakeCalculator.GetIntensity(6, r, 10, longitude) >= HuaniaEarthQuakeCalculator.GetIntensity(6, r + 1, 10, longitude));
            Check(HuaniaEarthQuakeCalculator.GetIntensity(6, 100, 10, longitude) < HuaniaEarthQuakeCalculator.GetIntensity(7, 100, 10, longitude));
        }
    }),
    ("Unsupported model inputs are unknown rather than invented values", () =>
    {
        Check(double.IsNaN(HuaniaEarthQuakeCalculator.GetIntensity(6, 100, null, 118)));
        Check(double.IsNaN(HuaniaEarthQuakeCalculator.GetIntensity(6, 100, 31, 118)));
        Check(double.IsNaN(HuaniaEarthQuakeCalculator.GetCountDownSeconds(31, 100)));
        Check(double.IsNaN(HuaniaEarthQuakeCalculator.GetCountDownSeconds(10, 501)));
        var v = One(Build(first, official));
        var s = new AlertSnapshot(v with { Preferred = official with { Depth = null } }, 35, 118);
        Check(!s.ModelSupported && s.Arrival is null && double.IsNaN(s.LocalIntensity));
        Check(!new AlertSnapshot(v, 35, 139).ModelSupported);
    }),
    ("Travel table interpolates distance and depth without extrapolation", () =>
    {
        Check(Math.Abs(HuaniaEarthQuakeCalculator.GetCountDownSeconds(0, 22.264) - 6.619) < 1e-12);
        Check(Math.Abs(HuaniaEarthQuakeCalculator.GetCountDownSeconds(2.5, 0) - 0.744) < 1e-12);
        Check(double.IsFinite(HuaniaEarthQuakeCalculator.GetCountDownSeconds(30, 500)));
    }),
    ("Three severity patterns retain distinct boundaries", () =>
    {
        Check(AlertSeverity.FromIntensity(2.99).Beeps == 0);
        Check(AlertSeverity.FromIntensity(3).Beeps == 1);
        Check(AlertSeverity.FromIntensity(5.99).Beeps == 1);
        Check(AlertSeverity.FromIntensity(6).Beeps == 2);
    }),
    ("CENC retains official magnitude sequence ID and unknown depth", () =>
    {
        var r = SeismicParser.Parse("cenc-early", "{\"code\":0,\"data\":{\"spot_infos\":[{\"third_id\":\"stable\",\"serial_number\":3,\"created_at\":1794586672,\"latitude\":42.221,\"longitude\":83.663,\"level\":\"5.2\"}]}}").Single();
        Check(r.EventId == "stable" && r.ReportNumber == 3 && r.Depth is null && r.Magnitude == 5.2);
    }),
    ("MIUI milliseconds and numeric training flags retained", () =>
    {
        var r = SeismicParser.Parse("miui", "{\"code\":0,\"data\":[{\"eventId\":202611140017,\"update\":3,\"startAt\":1794586672500,\"updateAt\":1794586716000,\"latitude\":42.221,\"longitude\":83.663,\"magnitude\":5.2,\"depth\":0,\"isTest\":1}]}").Single();
        Check(r.Depth is null && r.OriginTime.Millisecond == 500 && r.Training);
    }),
    ("Business error is never an affirmative empty response", () =>
    {
        try { SeismicParser.Parse("miui", "{\"code\":995}"); throw new Exception("Error swallowed"); } catch (JsonException) { }
    })
};
foreach (var test in cases) { test.Test(); Console.WriteLine($"PASS {test.Name}"); }
Console.WriteLine($"{cases.Length} regression checks passed.");
await StationChecks.RunAsync();
ConsensusEngine Build(params SeismicReport[] reports) { var e = new ConsensusEngine(); e.AddBatch(reports, now); return e; }
VerifiedEarthquake One(ConsensusEngine e) => e.GetEvents(now, "combined").Single();
void Check(bool condition) { if (!condition) throw new Exception("Regression check failed"); }
