using System.Diagnostics;
using NAudio.CoreAudioApi;
using Windows.Media.Control;

namespace EarthquakeWarning.Services;

/// <summary>Temporarily silences other Windows audio sessions. No application is killed or forcibly resumed.</summary>
internal sealed class AlertAudioFocus : IDisposable
{
    private static readonly object QueueGate = new();
    private static Task _tail = Task.CompletedTask;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;
    public Task Ready => _ready.Task;

    public AlertAudioFocus(bool suppressOthers, bool maximize, Action<string> status)
    {
        lock (QueueGate)
        {
            var previous = _tail;
            _run = _tail = Task.Run(async () =>
            {
                try { await previous; } catch (Exception error) { Debug.WriteLine(error); }
                await RunAsync(suppressOthers, maximize, status);
            });
        }
        AppDomain.CurrentDomain.ProcessExit += ProcessExit;
    }

    private async Task RunAsync(bool suppressOthers, bool maximize, Action<string> status)
    {
        var devices = new Dictionary<string, MMDevice>();
        var sessions = new Dictionary<string, (AudioSessionControl Session, bool Muted, float Volume, bool Own)>();
        var endpoints = new Dictionary<string, (AudioEndpointVolume Volume, bool Muted, float Level)>();
        var paused = suppressOthers ? PauseMediaAsync(_stop.Token, status) : Task.CompletedTask;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            while (!_stop.IsCancellationRequested)
            {
                var available = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var candidate in available)
                {
                    if (devices.ContainsKey(candidate.ID)) candidate.Dispose();
                    else devices[candidate.ID] = candidate;
                }
                var silenced = true;
                foreach (var device in devices.Values)
                {
                    try
                    {
                        var manager = device.AudioSessionManager;
                        manager.RefreshSessions();
                        for (var i = 0; i < manager.Sessions.Count; i++)
                        {
                            var session = manager.Sessions[i];
                            var key = session.GetSessionInstanceIdentifier;
                            if (sessions.ContainsKey(key)) { session.Dispose(); continue; }
                            var own = session.GetProcessID == Environment.ProcessId;
                            var volume = session.SimpleAudioVolume;
                            sessions[key] = (session, volume.Mute, volume.Volume, own);
                            if (own && maximize) { volume.Mute = false; volume.Volume = 1; }
                            else if (!own && suppressOthers) volume.Mute = true;
                        }
                    }
                    catch (Exception error) { silenced = false; status($"部分音频会话无法控制：{error.Message}"); }
                }
                _stop.Token.ThrowIfCancellationRequested();
                if (maximize && (!suppressOthers || silenced))
                {
                    foreach (var role in new[] { Role.Multimedia, Role.Console })
                    {
                        using var current = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
                        if (!endpoints.ContainsKey(current.ID) && devices.TryGetValue(current.ID, out var device))
                        {
                            var volume = device.AudioEndpointVolume;
                            endpoints[current.ID] = (volume, volume.Mute, volume.MasterVolumeLevelScalar);
                            volume.Mute = false;
                            volume.MasterVolumeLevelScalar = 1;
                        }
                    }
                }
                _ready.TrySetResult();
                await Task.Delay(250, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { status($"音频接管失败：{error.Message}"); }
        finally
        {
            _ready.TrySetResult();
            // Reduce endpoint volume before releasing application mutes; never briefly expose music at 100%.
            foreach (var saved in endpoints.Values)
                try { saved.Volume.MasterVolumeLevelScalar = saved.Level; saved.Volume.Mute = saved.Muted; }
                catch (Exception error) { status($"系统音量恢复失败：{error.Message}"); }
            foreach (var saved in sessions.Values)
            {
                try
                {
                    if (saved.Own && maximize) saved.Session.SimpleAudioVolume.Volume = saved.Volume;
                    if ((saved.Own && maximize) || (!saved.Own && suppressOthers)) saved.Session.SimpleAudioVolume.Mute = saved.Muted;
                }
                catch (Exception error) { status($"应用音量恢复失败：{error.Message}"); }
                finally
                {
                    try { saved.Session.Dispose(); } catch (Exception error) { Debug.WriteLine(error); }
                }
            }
            foreach (var device in devices.Values)
                try { device.Dispose(); } catch (Exception error) { Debug.WriteLine(error); }
            try { await paused; } catch (Exception error) { Debug.WriteLine(error); }
        }
    }

    private static async Task PauseMediaAsync(CancellationToken token, Action<string> status)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(token);
            foreach (var session in manager.GetSessions())
            {
                token.ThrowIfCancellationRequested();
                if (session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    if (!await session.TryPauseAsync().AsTask(token)) status("部分媒体不支持暂停，已保留应用静音");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { status($"媒体暂停不可用，保留应用静音：{error.Message}"); }
    }

    private void ProcessExit(object? sender, EventArgs args)
    {
        _stop.Cancel();
        try { _run.Wait(TimeSpan.FromSeconds(1)); } catch (Exception error) { Debug.WriteLine(error); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        AppDomain.CurrentDomain.ProcessExit -= ProcessExit;
        _stop.Cancel();
        _ = _run.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
        // The worker owns COM objects and restores them on its own thread.
    }
}
