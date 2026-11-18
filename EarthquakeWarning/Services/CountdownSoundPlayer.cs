using Avalonia.Platform;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Concurrent;

namespace EarthquakeWarning.Services;

/// <summary>Fixed offline number clips and zero/single/double beep patterns in a bounded one-second slot.</summary>
public static class CountdownSoundPlayer
{
    private const int Rate = 24000;
    private static readonly ConcurrentDictionary<int, float[]> Numbers = new();

    public static byte[] BuildWave(int number, int beeps)
    {
        var spoken = Numbers.GetOrAdd(number, LoadNumber);
        var slot = number > 10 ? 1.94 : 0.94;
        var samples = new float[(int)(Rate * slot)];
        // Assets are authored to fit. Never truncate syllables or change pitch by resampling spoken numbers.
        if (spoken.Length > Rate * (slot - 0.26)) throw new InvalidDataException("报数音频超出时隙");
        for (var i = 0; i < spoken.Length; i++) samples[i + 240] = spoken[i] * 0.95f;
        for (var beep = 0; beep < Math.Clamp(beeps, 0, 2); beep++)
        {
            var start = (int)(Rate * (slot - 0.23 + beep * 0.1));
            var count = (int)(Rate * 0.065);
            for (var i = 0; i < count; i++)
            {
                var envelope = Math.Min(1, Math.Min(i / 100.0, (count - i) / 100.0));
                samples[start + i] += (float)(0.28 * Math.Sin(2 * Math.PI * 1000 * i / Rate) * envelope);
            }
        }
        using var output = new MemoryStream();
        using (var writer = new WaveFileWriter(output, WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1)))
            writer.WriteSamples(samples, 0, samples.Length);
        return output.ToArray();
    }

    public static byte[] BuildWarningWave(int beeps, bool urgent = false)
    {
        var duration = urgent ? 0.35 : beeps >= 2 ? 1.2 : beeps == 1 ? 0.9 : 0.6;
        var samples = new float[(int)(Rate * duration)];
        double phase = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var time = (double)i / Rate;
            var sweep = (time % 0.3) / 0.3;
            phase += 2 * Math.PI * (650 + 500 * sweep) / Rate;
            var envelope = Math.Min(1, Math.Min(time / 0.02, (duration - time) / 0.05));
            var pulse = (time % 0.3) < 0.25 ? 1.0 : 0.15;
            samples[i] = (float)(0.40 * envelope * pulse * (Math.Sin(phase) + 0.15 * Math.Sin(phase * 2)));
        }
        using var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(stream, WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1)))
            writer.WriteSamples(samples, 0, samples.Length);
        return stream.ToArray();
    }

    public static async Task PlayWarningAsync(int beeps, bool urgent, CancellationToken token)
    {
        using var stream = new MemoryStream(BuildWarningWave(beeps, urgent));
        using var reader = new WaveFileReader(stream);
        await PlayProviderAsync(reader, token);
    }

    public static async Task PlayAsync(int number, int beeps, CancellationToken token)
    {
        using var stream = new MemoryStream(BuildWave(number, beeps));
        using var reader = new WaveFileReader(stream);
        var panned = new PanningSampleProvider(reader.ToSampleProvider()) { Pan = 0.35f };
        await PlayProviderAsync(panned.ToWaveProvider(), token);
    }

    private static float[] LoadNumber(int number)
    {
        if (number is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(number));
        using var stream = AssetLoader.Open(new Uri($"avares://EarthquakeWarning/Assets/Countdown/{number}.wav"));
        using var reader = new WaveFileReader(stream);
        ISampleProvider source = reader.ToSampleProvider();
        if (source.WaveFormat.Channels == 2) source = new StereoToMonoSampleProvider(source);
        if (source.WaveFormat.SampleRate != Rate) source = new WdlResamplingSampleProvider(source, Rate);
        var samples = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0) samples.AddRange(buffer.Take(read));
        var start = samples.FindIndex(sample => Math.Abs(sample) > 0.003f);
        var end = samples.FindLastIndex(sample => Math.Abs(sample) > 0.003f);
        if (start < 0 || end <= start) throw new InvalidDataException("离线报数音频为空");
        return samples.Skip(Math.Max(0, start - 120)).Take(end - start + 240).ToArray();
    }

    public static async Task PlayAnnouncementAsync(string name, CancellationToken token)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://EarthquakeWarning/Assets/Countdown/{name}.wav"));
        using var reader = new WaveFileReader(stream);
        await PlayProviderAsync(reader, token);
    }

    private static async Task PlayProviderAsync(IWaveProvider source, CancellationToken token)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        using var output = new WasapiOut(device, AudioClientShareMode.Shared, false, 40);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Stopped(object? sender, StoppedEventArgs args)
        {
            if (args.Exception is not null) finished.TrySetException(args.Exception);
            else finished.TrySetResult();
        }
        output.PlaybackStopped += Stopped;
        try
        {
            output.Init(source);
            output.Play();
            await finished.Task.WaitAsync(token);
        }
        finally { output.Stop(); output.PlaybackStopped -= Stopped; }
    }
}
