using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace EarthquakeWarning.Models;

public partial class EarthquakeNotificationSettings : ObservableRecipient
{
    [ObservableProperty]
    private string _sourceMode = "combined";
    [ObservableProperty]
    private bool _enableFullScreen = true;
    [ObservableProperty]
    private bool _monitoringEnabled = true;
    [ObservableProperty]
    private bool _enableOnlineMap = true;
    [ObservableProperty]
    private string _mapTileTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    [ObservableProperty]
    private string _mapAttribution = "© OpenStreetMap contributors";
    [ObservableProperty]
    private bool _allowConcurrentSpeech = true;
    [ObservableProperty]
    private bool _useRecordedCountdown = true;
    [ObservableProperty]
    private string _drillProfile = "felt";
    [ObservableProperty]
    private string _informationVoice = "自动选择";
    [ObservableProperty]
    private string _countdownVoice = "自动选择";
    [ObservableProperty]
    private bool _boostVolume = true;
    [ObservableProperty]
    private string _sourceStatus = "正在连接地震接口…";
    [ObservableProperty]
    private string _recentReports = "等待接口返回震情记录";
    [ObservableProperty]
    private string _speechStatus = "等待预警";
    [ObservableProperty]
    private double _latitude = 0;
    [ObservableProperty]
    private double _longitude = 0;
    [ObservableProperty]
    private double _threshold = 2.0;
    [ObservableProperty]
    private bool _isSpeechEnabled = true;
    [ObservableProperty]
    private double _speechThreshold = 2.0;
    [ObservableProperty]
    private string _info = "";
    [ObservableProperty]
    private EarthquakeInfo earthquakeInfo = new();
    [ObservableProperty]
    private string _serverInfo = "";
    [ObservableProperty]
    private bool _isCommandEnabled;
    [ObservableProperty]
    private string _command = "";
    [ObservableProperty]
    private string _actionStatus = "尚未执行预警动作";
    /// <summary>
    /// 预警演练期间为 true，用于阻止真实预警动作（如关机命令）被执行。
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    [NotifyPropertyChangedFor(nameof(DrillButtonText))]
    private bool _isDrillMode;

    public string DrillButtonText => IsDrillMode ? "停止演练" : "预警演练";
}
