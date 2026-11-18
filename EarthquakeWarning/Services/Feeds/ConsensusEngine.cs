using EarthquakeWarning.Models;

namespace EarthquakeWarning.Services.Feeds;

/// <summary>Cross-checks delivery channels. Matching relays can still share the same seismic publisher.</summary>
public sealed class ConsensusEngine
{
    private readonly object _gate = new();
    private readonly List<EventState> _events = new();

    public void Add(SeismicReport report, DateTimeOffset now)
    {
        if (report.Training || now - report.OriginTime > TimeSpan.FromMinutes(5)
            || report.OriginTime - now > TimeSpan.FromSeconds(10)
            || (!report.Cancelled && !report.HasValidParameters)) return;
        lock (_gate)
        {
            _events.RemoveAll(item => now - item.Origin > TimeSpan.FromMinutes(6));
            var state = _events.FirstOrDefault(item => item.Reports.Any(existing => existing.Key == report.Key));
            state ??= _events.Where(item => !item.Reports.Any(existing => existing.Gateway == report.Gateway
                    && existing.Feed == report.Feed && existing.EventId != report.EventId))
                .Where(item => item.Reports.Any(existing => SameEvent(existing, report)))
                .OrderBy(item => Math.Abs((item.Origin - report.OriginTime).TotalSeconds)).FirstOrDefault();
            if (state is null)
            {
                if (report.Cancelled) return;
                state = new EventState(Guid.NewGuid().ToString("N"), report.OriginTime);
                _events.Add(state);
                if (_events.Count > 100) _events.RemoveAt(0);
            }
            var previous = state.Reports.FindIndex(existing => existing.Feed == report.Feed && existing.Gateway == report.Gateway);
            if (previous >= 0)
            {
                var old = state.Reports[previous];
                if (old.Cancelled && !report.Cancelled) return;
                if (report.Cancelled && old.ReportNumber.HasValue && report.ReportNumber < old.ReportNumber) return;
                if (!report.Cancelled && IsOlderOrEqual(old, report)) return;
                state.Reports[previous] = report;
            }
            else state.Reports.Add(report);
        }
    }

    public IReadOnlyList<VerifiedEarthquake> GetEvents(DateTimeOffset now, string mode)
    {
        lock (_gate)
        {
            return _events.Where(item => now - item.Origin < TimeSpan.FromMinutes(5))
                .Select(item => Evaluate(item, mode)).Where(item => item is not null)
                .Cast<VerifiedEarthquake>().ToArray();
        }
    }

    private static VerifiedEarthquake? Evaluate(EventState state, string mode)
    {
        var reports = state.Reports.Where(report => mode == "combined" || report.Feed == mode
            || (mode == "wolfx-live" && report.Gateway == "Wolfx")).ToArray();
        if (reports.Length == 0) return null;
        var cancelled = reports.Any(report => report.Cancelled);
        var current = reports.Where(report => !report.Cancelled).ToArray();
        if (current.Length == 0) current = reports;
        // Use the medoid to avoid turning one outlying high-magnitude report into a warning for every channel.
        var preferred = current.OrderByDescending(candidate => current.Count(other => Compatible(candidate, other)))
            .ThenByDescending(report => report.Kind == ReportKind.Bulletin)
            .ThenByDescending(report => report.Gateway == "CENC")
            .ThenByDescending(report => report.ReportNumber ?? 0).ThenByDescending(report => report.ReportTime).First();
        var agreeing = current.Where(report => Compatible(preferred, report)).ToArray();
        var channels = agreeing.Select(report => report.Gateway).Distinct().Count();
        // A contradictory active report keeps the full-screen alarm and external actions on hold.
        var conflict = current.Any(first => current.Any(second => !Compatible(first, second)));
        return new VerifiedEarthquake(state.Id, preferred, reports, channels, conflict, cancelled);
    }

    public static bool SameEvent(SeismicReport first, SeismicReport second) =>
        Math.Abs((first.OriginTime - second.OriginTime).TotalSeconds) <= 15
        && Distance(first.Latitude, first.Longitude, second.Latitude, second.Longitude) <= 60;

    public static bool Compatible(SeismicReport first, SeismicReport second) => SameEvent(first, second)
        && Math.Abs(first.Magnitude - second.Magnitude) <= 1.0
        && (first.Depth is null || second.Depth is null || Math.Abs(first.Depth.Value - second.Depth.Value) <= 60);

    private static bool IsOlderOrEqual(SeismicReport previous, SeismicReport candidate)
    {
        if (previous.ReportNumber.HasValue && candidate.ReportNumber.HasValue)
        {
            if (candidate.ReportNumber != previous.ReportNumber) return candidate.ReportNumber < previous.ReportNumber;
            return candidate == previous || candidate.ReportTime < previous.ReportTime;
        }
        if (candidate.ReportTime != previous.ReportTime) return candidate.ReportTime < previous.ReportTime;
        return candidate == previous;
    }

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
    }
}
