using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Platforms.Abstraction;
using EarthquakeWarning.Models;
using System.Diagnostics;
using System.Text.Json;
using System.Speech.Synthesis;
using EarthquakeWarning.Services.NotificationProviders;

namespace EarthquakeWarning.Controls.NotificationProviders;

public partial class EarthquakeNotificationProviderSettingsControl : NotificationProviderControlBase<EarthquakeNotificationSettings>
{
    public IReadOnlyList<SourceOption> SourceOptions => SourceOption.All;
    public IReadOnlyList<SourceOption> DrillOptions { get; } = new[]
    {
        new SourceOption("weak", "无感或微弱震感"), new SourceOption("felt", "有感无破坏"), new SourceOption("damaging", "有感有破坏")
    };
    public IReadOnlyList<string> Voices { get; } = GetVoices();

    private static IReadOnlyList<string> GetVoices()
    {
        try
        {
            using var speech = new SpeechSynthesizer();
            return new[] { "自动选择" }.Concat(speech.GetInstalledVoices().Where(voice => voice.Enabled
                && voice.VoiceInfo.Gender == VoiceGender.Female
                && voice.VoiceInfo.Culture.TwoLetterISOLanguageName == "zh").Select(voice => voice.VoiceInfo.Name)).ToArray();
        }
        catch (Exception error) { Debug.WriteLine(error); return new[] { "自动选择" }; }
    }
    public EarthquakeNotificationProviderSettingsControl()
    {
        InitializeComponent();
    }

    private void btnExample_Click(object sender, RoutedEventArgs e)
    {
        var provider = EarthquakeNotificationProvider.Current;
        if (provider is null) { Settings.Info = "提醒提供方尚未启动，请稍后重试"; return; }
        if (Settings.IsDrillMode) provider.StopDrill();
        else provider.StartDrill();
    }

    private void OpenStations(object? sender, RoutedEventArgs e)
    {
        var provider = EarthquakeNotificationProvider.Current;
        if (provider is null) { Settings.StationStatus = "提醒提供方尚未启动，请稍后重试"; return; }
        provider.OpenStationMonitor();
    }

    private async void btnLocate_Click(object? sender, RoutedEventArgs e)
    {
        btnLocate.IsEnabled = false;
        locationErrorInfoBar.IsOpen = false;

        try
        {
            var location = await PlatformServices.LocationService.GetLocationAsync();
            if (location is null)
            {
                locationErrorInfoBar.IsOpen = true;
                return;
            }

            Settings.Longitude = location.Longitude;
            Settings.Latitude = location.Latitude;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"自动定位失败: {ex}");
            locationErrorInfoBar.IsOpen = true;
        }
        finally
        {
            btnLocate.IsEnabled = true;
        }
    }
}
