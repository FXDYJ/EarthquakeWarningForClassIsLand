using EarthquakeWarning.Models;

namespace EarthquakeWarning.Services.Feeds;

/// <summary>Fail-closed verification; delivery gateways are not independent seismic observations.</summary>
public sealed class ConsensusEngine(bool simulation = false)
{
    private readonly object _gate = new();
    private readonly List<EventState> _events = new();
    private readonly Dictionary<string, DateTimeOffset> _cancelled = new();
    private readonly HashSet<string> _unresolvedCancellations = new();

    public void ResetVotes()
    {
        lock (_gate)
            foreach (var state in _events) state.ActiveKeys.Clear();
    }

    public void Add(SeismicReport report, DateTimeOffset now) => AddBatch(new[] { report }, now);

    // Apply the complete latest response under one lock, including cancellation/rewrite conflicts.
    public void AddBatch(IEnumerable<SeismicReport> reports, DateTimeOffset now)
    {
        var batch = reports.GroupBy(report => report.Key).SelectMany(group =>
        {
            var cancellations = group.Where(report => report.Cancelled).ToArray();
            if (cancellations.Length > 0) return cancellations;
            var maxSequence = group.Max(report => report.ReportNumber);
            var latest = group.Where(report => report.ReportNumber == maxSequence).ToArray();
            var maxTime = latest.Max(report => report.ReportTime);
            return latest.Where(report => report.ReportTime == maxTime).Distinct().ToArray();
        }).ToArray();
        lock (_gate)
        {
            _events.RemoveAll(state => now - state.Origin > TimeSpan.FromMinutes(6));
            foreach (var key in _cancelled.Where(pair => now - pair.Value > TimeSpan.FromMinutes(10)).Select(pair => pair.Key).ToArray())
            { _cancelled.Remove(key); _unresolvedCancellations.Remove(key); }
            foreach (var report in batch) Apply(report, now);
        }
    }

    private void Apply(SeismicReport report, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(report.EventId)) return;
        var state = _events.FirstOrDefault(item => item.Keys.Contains(report.Key));
        if (report.Cancelled)
        {
            // Even a regressed cancellation is a veto: a missed alarm is preferable to reviving a withdrawn event.
            state ??= _events.FirstOrDefault(item => item.Reports.Any(existing => SameEvent(existing, report)));
            _cancelled.TryAdd(report.Key, now);
            if (state is null && (report.OriginTime == DateTimeOffset.MinValue
                || now - report.OriginTime <= TimeSpan.FromMinutes(5))) _unresolvedCancellations.Add(report.Key);
            if (_cancelled.Count > 2000)
            {
                var oldest = _cancelled.MinBy(pair => pair.Value).Key;
                _cancelled.Remove(oldest); _unresolvedCancellations.Remove(oldest);
            }
            if (state is not null) state.Cancelled = true;
            return;
        }
        if (_cancelled.ContainsKey(report.Key))
        {
            state ??= _events.FirstOrDefault(item => item.Reports.Any(existing => SameEvent(existing, report)));
            if (state is not null) { state.Cancelled = true; state.Keys.Add(report.Key); _unresolvedCancellations.Remove(report.Key); }
            return;
        }
        var valid = !report.Training && report.HasValidParameters && (report.ReportNumber is null or > 0)
            && now - report.OriginTime <= TimeSpan.FromMinutes(5) && report.OriginTime <= now
            && report.ReportTime >= report.OriginTime && report.ReportTime <= now.AddSeconds(5);
        if (!valid)
        {
            if (state is not null) state.InvalidKeys.Add(report.Key);
            return;
        }
        var candidates = _events.Where(item => !item.Reports.Any(existing => existing.Gateway == report.Gateway
                && existing.Feed == report.Feed && existing.EventId != report.EventId))
            .Where(item => item.Reports.Any(existing => SameEvent(existing, report)))
            .OrderBy(item => Math.Abs((item.Origin - report.OriginTime).TotalSeconds)).ToArray();
        if (state is null && candidates.Length > 1)
            foreach (var candidate in candidates) candidate.InvalidKeys.Add("ambiguous-association");
        state ??= candidates.FirstOrDefault();
        if (state is null)
        {
            state = new EventState(Guid.NewGuid().ToString("N"), report.OriginTime);
            _events.Add(state);
            if (_events.Count > 100) _events.RemoveAt(0);
        }
        state.Keys.Add(report.Key);
        var index = state.Reports.FindIndex(existing => existing.Key == report.Key);
        if (index < 0) { state.Reports.Add(report); state.ActiveKeys.Add(report.Key); state.Seen[report.Key] = now; return; }
        var old = state.Reports[index];
        if (old.ReportNumber.HasValue && report.ReportNumber.HasValue && report.ReportNumber < old.ReportNumber) return;
        if (report.ReportTime < old.ReportTime) return;
        if ((old.ReportNumber == report.ReportNumber || old.ReportNumber.HasValue && !report.ReportNumber.HasValue)
            && old with { ReportTime = report.ReportTime } != report)
        {
            state.InvalidKeys.Add(report.Key);
            return;
        }
        if (old.ReportNumber == report.ReportNumber && old.ReportTime == report.ReportTime)
        {
            if (old != report) state.InvalidKeys.Add(report.Key);
            else { state.ActiveKeys.Add(report.Key); state.Seen[report.Key] = now; }
            return;
        }
        state.Reports[index] = report;
        state.ActiveKeys.Add(report.Key);
        state.Seen[report.Key] = now;
    }

    public IReadOnlyList<VerifiedEarthquake> GetEvents(DateTimeOffset now, string mode)
    {
        lock (_gate)
            return _events.Where(item => now - item.Origin < TimeSpan.FromMinutes(5))
                .Select(item => Evaluate(item, mode, now)).OfType<VerifiedEarthquake>().ToArray();
    }

    private VerifiedEarthquake? Evaluate(EventState state, string mode, DateTimeOffset now)
    {
        var reports = state.Reports.Where(report => state.ActiveKeys.Contains(report.Key)).Where(report => mode == "combined" || report.Feed == mode
            || (mode == "wolfx-live" && report.Gateway == "Wolfx")).ToArray();
        if (reports.Length == 0) return null;
        var preferred = reports.OrderByDescending(report => report.IsOfficialBulletin)
            .ThenByDescending(report => reports.Count(other => Compatible(report, other)))
            .ThenByDescending(report => report.ReportNumber ?? 0).ThenByDescending(report => report.ReportTime).First();
        var conflict = _unresolvedCancellations.Any(key => now - _cancelled[key] <= TimeSpan.FromMinutes(5)) || state.InvalidKeys.Count > 0
            || reports.Any(first => reports.Any(second => !Compatible(first, second)));
        var channels = reports.Where(report => Compatible(preferred, report)).Select(report => report.Gateway).Distinct().Count();
        var expired = reports.Any(report => now - state.Seen.GetValueOrDefault(report.Key) > TimeSpan.FromSeconds(30));
        return new(state.Id, preferred, reports, channels, conflict, state.Cancelled, simulation,
            expired ? "接口证据已过期，等待刷新" : "");
    }

    // Broad association retains contradictory revisions so they veto escalation.
    public static bool SameEvent(SeismicReport first, SeismicReport second) =>
        Math.Abs((first.OriginTime - second.OriginTime).TotalSeconds) <= 15
        && Distance(first.Latitude, first.Longitude, second.Latitude, second.Longitude) <= 60;

    public static bool Compatible(SeismicReport first, SeismicReport second) =>
        Math.Abs((first.OriginTime - second.OriginTime).TotalSeconds) <= 5
        && Distance(first.Latitude, first.Longitude, second.Latitude, second.Longitude) <= 20
        && Math.Abs(first.Magnitude - second.Magnitude) <= 0.300000001
        && (first.Depth is null || second.Depth is null || Math.Abs(first.Depth.Value - second.Depth.Value) <= 15);

    public static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        const double radians = Math.PI / 180;
        var a = Math.Pow(Math.Sin((lat2 - lat1) * radians / 2), 2)
            + Math.Cos(lat1 * radians) * Math.Cos(lat2 * radians) * Math.Pow(Math.Sin((lon2 - lon1) * radians / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }

    private sealed record EventState(string Id, DateTimeOffset Origin)
    {
        public List<SeismicReport> Reports { get; } = new();
        public HashSet<string> Keys { get; } = new();
        public HashSet<string> ActiveKeys { get; } = new();
        public Dictionary<string, DateTimeOffset> Seen { get; } = new();
        public HashSet<string> InvalidKeys { get; } = new();
        public bool Cancelled { get; set; }
    }
}
