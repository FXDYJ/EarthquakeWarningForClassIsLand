using EarthquakeWarning.Calculators;

namespace EarthquakeWarning.Models;

public sealed record AlertSnapshot(VerifiedEarthquake Event, double UserLatitude, double UserLongitude, bool Drill = false)
{
    public SeismicReport Report => Event.Preferred;
    public double Distance => Services.Feeds.ConsensusEngine.Distance(UserLatitude, UserLongitude, Report.Latitude, Report.Longitude);
    public double LocalIntensity => HuaniaEarthQuakeCalculator.GetIntensity(Report.Magnitude, Distance, Report.Depth, Report.Longitude);
    public DateTimeOffset Arrival => Report.OriginTime.AddSeconds(HuaniaEarthQuakeCalculator.GetCountDownSeconds(Report.Depth ?? 10, Distance));
    public double Remaining => (Arrival - DateTimeOffset.UtcNow).TotalSeconds;
    public string EventKey => Event.Id;
    public AlertSeverity Severity => AlertSeverity.FromIntensity(LocalIntensity);
}
