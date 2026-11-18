namespace EarthquakeWarning.Models;

public enum AlertImpact { Weak, Felt, Damaging }

public sealed record AlertSeverity(AlertImpact Impact, string Name, string Accent, string Header, string Surface, int Beeps, string Advice)
{
    // Three user-facing sound profiles based on estimated local intensity, not source magnitude.
    public static AlertSeverity FromIntensity(double intensity) => intensity switch
    {
        >= 6 => new(AlertImpact.Damaging, "有感有破坏（预估）", "#FF485A", "#C32335", "#351019", 2, "伏地 · 遮挡 · 手抓牢"),
        >= 3 => new(AlertImpact.Felt, "有感无破坏（预估）", "#FFBD66", "#885414", "#362813", 1, "注意坠落物 · 远离窗户 · 就近避险"),
        _ => new(AlertImpact.Weak, "无感或微弱震感（预估）", "#65CBF3", "#175577", "#102B3B", 0, "保持正常活动，持续关注官方震情")
    };
}
