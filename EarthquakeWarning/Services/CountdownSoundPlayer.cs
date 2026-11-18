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
        var samples = new float[(int)(Rate * 0.9)];
        // Keep the speech clear and reserve the end of the second for the familiar beep pattern.
        var length = Math.Min(spoken.Length, (int)(Rate * 0.65));
        for (var i = 0; i < length; i++)
        {
            var position = i * (spoken.Length - 1.0) / Math.Max(1, length - 1);
            samples[i] = spoken[(int)position] * 0.9f;
        }
        for (var beep = 0; beep < Math.Clamp(beeps, 0, 2); beep++)
        {
            var start = (int)(Rate * (0.71 + beep * 0.1));
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
        using var output = new WasapiOut(AudioClientShareMode.Shared, false, 40);
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
