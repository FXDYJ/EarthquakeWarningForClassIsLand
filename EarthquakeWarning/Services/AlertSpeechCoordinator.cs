using System.Diagnostics;
using System.Globalization;
using System.Speech.Synthesis;
using EarthquakeWarning.Models;
using ClassIsland.Core.Abstractions.Services.SpeechService;
using ClassIsland.Shared;

namespace EarthquakeWarning.Services;

/// <summary>Information and countdown have separate voices. The last ten seconds reserve the speech space for countdown.</summary>
public sealed class AlertSpeechCoordinator : IDisposable
{
    private CancellationTokenSource? _session;
    private Task _running = Task.CompletedTask;
    public event Action<string>? StatusChanged;

    public void Start(Func<AlertSnapshot?> current, EarthquakeNotificationSettings settings)
    {
        Stop();
        ClearHostSpeech();
        var cancellation = new CancellationTokenSource();
        _session = cancellation;
        var previous = _running;
        _running = Task.Run(async () =>
        {
            try { await previous.ConfigureAwait(false); }
            catch (Exception error) { Debug.WriteLine(error); }
            try { await RunAsync(current, settings, cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error) { StatusChanged?.Invoke($"本地语音不可用：{error.Message}"); }
            finally { cancellation.Dispose(); }
        });
    }

    public static void ClearHostSpeech()
    {
        try { IAppHost.TryGetService<ISpeechService>()?.ClearSpeechQueue(); }
        catch (Exception error) { Debug.WriteLine($"无法清空 CI 语音队列：{error.Message}"); }
    }

    public void Stop()
    {
        var session = Interlocked.Exchange(ref _session, null);
        try { session?.Cancel(); }
        catch (ObjectDisposedException) { /* The completed session already released its synthesizers. */ }
    }

    private async Task RunAsync(Func<AlertSnapshot?> current, EarthquakeNotificationSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var first = current();
        if (first is null || !first.Event.Confirmed || !first.ModelSupported) return;
        try { await CountdownSoundPlayer.PlayWarningAsync(first.Severity.Beeps, first.Remaining <= 6, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) { StatusChanged?.Invoke($"提示音不可用：{error.Message}"); }
        using var information = new SpeechSynthesizer();
        using var countdown = new SpeechSynthesizer();
        var voices = information.GetInstalledVoices().Where(voice => voice.Enabled
            && voice.VoiceInfo.Gender == VoiceGender.Female
            && voice.VoiceInfo.Culture.TwoLetterISOLanguageName == "zh").Select(voice => voice.VoiceInfo.Name).ToArray();
        if (voices.Length == 0)
        {
            StatusChanged?.Invoke("系统未安装中文女声音色，使用内置女声提示和报数");
            await RunRecordedOnlyAsync(current, token);
            return;
        }
        var informationVoice = voices.Contains(settings.InformationVoice) ? settings.InformationVoice : voices[0];
        var countdownVoice = voices.Contains(settings.CountdownVoice) ? settings.CountdownVoice
            : voices.FirstOrDefault(voice => voice != informationVoice) ?? informationVoice;
        information.SelectVoice(informationVoice);
        countdown.SelectVoice(countdownVoice);
        information.Rate = 1;
        information.Volume = 90;
        countdown.Rate = 1;
        countdown.Volume = 100;
        var simultaneous = settings.AllowConcurrentSpeech && (settings.UseRecordedCountdown || informationVoice != countdownVoice);
        var recordedCountdown = settings.UseRecordedCountdown;
        StatusChanged?.Invoke(settings.UseRecordedCountdown ? $"信息：{informationVoice}；报数：离线数字音频 + 按烈度单/双滴声，最后 10 秒暂停信息"
            : simultaneous ? $"信息：{informationVoice}；倒计时：{countdownVoice}（最后 10 秒暂停信息播报）"
            : $"使用 {informationVoice}，信息与倒计时错峰播报");
        var snapshot = current();
        if (snapshot is null) return;
        var drill = snapshot.Drill ? "演练，" : "";
        var instruction = snapshot.Severity.Impact == AlertImpact.Weak ? "请保持关注"
            : snapshot.Severity.Impact == AlertImpact.Felt ? "请注意避险" : "立即就近避险";
        var opening = snapshot.Remaining <= 6
            ? $"{drill}地震预警，{instruction}。"
            : snapshot.Remaining <= 30
            ? $"{drill}地震预警，{instruction}，预计震动即将到达。"
            : $"{drill}地震预警，{snapshot.Report.PlaceName}发生{snapshot.Report.Magnitude.ToString("F1", CultureInfo.InvariantCulture)}级地震。{instruction}，注意接下来的震动到达倒计时。";
        // Explanation is always completed before any numerical countdown starts.
        await SpeakAsync(information, opening, token);
        var nextInformation = DateTimeOffset.UtcNow;
        var lastCount = int.MaxValue;
        Task informationTask = Task.CompletedTask;
        using var detailCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        while (!token.IsCancellationRequested)
        {
            snapshot = current();
            if (snapshot is null || snapshot.Event.Cancelled || !snapshot.Event.Confirmed) return;
            var remaining = (int)Math.Ceiling(snapshot.Remaining);
            if (remaining <= 0) break;
            if (remaining <= 10 && !detailCancellation.IsCancellationRequested)
            {
                detailCancellation.Cancel();
                await informationTask;
            }
            if (remaining > 15 && DateTimeOffset.UtcNow >= nextInformation && informationTask.IsCompleted)
            {
                nextInformation = DateTimeOffset.UtcNow.AddSeconds(18);
                var detail = $"震情信息，预估本地烈度{snapshot.LocalIntensity:F1}，震中距离{snapshot.Distance:F0}公里，{snapshot.Event.MatchingGateways}个接口已核对。请远离窗户，保护头部。";
                if (simultaneous && ((recordedCountdown && remaining <= 60) || informationVoice != countdownVoice))
                {
                    information.Volume = 60;
                    informationTask = SpeakOptionalDetailAsync(information, detail, detailCancellation.Token);
                }
                else if (remaining > 25)
                {
                    await SpeakOptionalDetailAsync(information, detail, token);
                    continue; // Re-read the absolute arrival time after the information phrase.
                }
            }
            if (remaining != lastCount && (remaining <= 60 ? remaining <= 10 || remaining % 2 == 0 : remaining % 5 == 0))
            {
                var text = remaining <= 10 ? remaining.ToString(CultureInfo.InvariantCulture) : $"到达，{remaining}秒";
                if (recordedCountdown && remaining is >= 1 and <= 60)
                {
                    try { await CountdownSoundPlayer.PlayAsync(remaining, snapshot.Severity.Beeps, token); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    {
                        recordedCountdown = false;
                        detailCancellation.Cancel();
                        await informationTask;
                        StatusChanged?.Invoke($"离线报数播放失败，改用系统语音：{error.Message}");
                        await SpeakAsync(countdown, text, token);
                    }
                }
                else await SpeakAsync(countdown, text, token);
                lastCount = remaining;
            }
            await Task.Delay(60, token);
        }
        detailCancellation.Cancel();
        await informationTask;
        if (!token.IsCancellationRequested)
        {
            var advice = snapshot?.Severity.Impact == AlertImpact.Weak ? "请继续关注官方后续信息" : "请保持就近避险，等待震动结束";
            await SpeakAsync(information, $"{drill}预计震动已到达，{advice}。", token);
        }
    }

    private static async Task SpeakOptionalDetailAsync(SpeechSynthesizer voice, string text, CancellationToken token)
    {
        try { await SpeakAsync(voice, text, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static async Task RunRecordedOnlyAsync(Func<AlertSnapshot?> current, CancellationToken token)
    {
        var snapshot = current();
        if (snapshot is null) return;
        var intro = snapshot.Remaining <= 6 ? (snapshot.Drill ? "intro-urgent-drill" : "intro-urgent")
            : snapshot.Drill ? "intro-drill" : "intro";
        await CountdownSoundPlayer.PlayAnnouncementAsync(intro, token);
        var last = int.MaxValue;
        while (!token.IsCancellationRequested)
        {
            snapshot = current();
            if (snapshot is null || !snapshot.Event.Confirmed) return;
            var remaining = (int)Math.Ceiling(snapshot.Remaining);
            if (remaining <= 0) break;
            if (remaining <= 60 && remaining != last && (remaining <= 10 || remaining % 2 == 0))
            {
                await CountdownSoundPlayer.PlayAsync(remaining, snapshot.Severity.Beeps, token);
                last = remaining;
            }
            await Task.Delay(30, token);
        }
        await CountdownSoundPlayer.PlayAnnouncementAsync(snapshot?.Drill == true ? "arrived-drill" : "arrived", token);
    }

    private static async Task SpeakAsync(SpeechSynthesizer voice, string text, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, SpeakCompletedEventArgs args)
        {
            if (args.Error is not null) completion.TrySetException(args.Error);
            else if (args.Cancelled) completion.TrySetCanceled(token);
            else completion.TrySetResult();
        }
        voice.SpeakCompleted += Completed;
        try
        {
            voice.SpeakAsync(text);
            using var registration = token.Register(voice.SpeakAsyncCancelAll);
            await completion.Task;
        }
        finally { voice.SpeakCompleted -= Completed; }
    }

    public void Dispose() => Stop();
}
