using EarthquakeWarning.Calculators;

namespace EarthquakeWarning.Models;

public sealed record AlertSnapshot(VerifiedEarthquake Event, double UserLatitude, double UserLongitude, bool Drill = false)
{
    public SeismicReport Report => Event.Preferred;
    public double Distance => Services.Feeds.ConsensusEngine.Distance(UserLatitude, UserLongitude, Report.Latitude, Report.Longitude);
    public bool ModelSupported => (Drill || (MainlandRegion.Contains(Report.Latitude, Report.Longitude)
        && MainlandRegion.Contains(UserLatitude, UserLongitude)))
        && Report.Depth is > 0 and <= 30 && Distance <= 500 && Report.Magnitude is >= 4 and <= 8.5;
    public double LocalIntensity => ModelSupported
        ? HuaniaEarthQuakeCalculator.GetIntensity(Report.Magnitude, Distance, Report.Depth, Report.Longitude) : double.NaN;
    public DateTimeOffset? Arrival => ModelSupported
        ? Report.OriginTime.AddSeconds(HuaniaEarthQuakeCalculator.GetCountDownSeconds(Report.Depth!.Value, Distance)) : null;
    public double Remaining => Arrival is { } arrival ? (arrival - DateTimeOffset.UtcNow).TotalSeconds : double.NaN;
    public string EventKey => Event.Id;
    public AlertSeverity Severity => AlertSeverity.FromIntensity(LocalIntensity);
}
