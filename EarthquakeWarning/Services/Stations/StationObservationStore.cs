using EarthquakeWarning.Models;

namespace EarthquakeWarning.Services.Stations;

/// <summary>Observation-only state. It has no connection to event confirmation or alert actions.</summary>
public sealed class StationObservationStore
{
    private readonly object _gate = new();
    private StationNetworkState _state = StationNetworkState.Disabled;
    public StationNetworkState Snapshot => Volatile.Read(ref _state);

    public void Change(Func<StationNetworkState, StationNetworkState> update)
    {
        lock (_gate) Volatile.Write(ref _state, update(_state));
    }

    public bool Apply(StationFrame frame, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_state.Enabled) return false;
            if (frame.Time - now > TimeSpan.FromSeconds(5) || now - frame.Time > TimeSpan.FromSeconds(10))
                throw new InvalidDataException("观测时标过期或超前，请检查系统时间");
            if (_state.Frame is { } previous && frame.Time <= previous.Time) return false;
            // Each RTS message is a complete replacement; missing stations must disappear immediately.
            Volatile.Write(ref _state, _state with { Frame = frame, Connected = true, Status = "观测流正常" });
            return true;
        }
    }
}
