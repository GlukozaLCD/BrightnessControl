namespace BrightnessControl.Core;

public sealed class AppProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AppMatchType MatchType { get; set; } = AppMatchType.ProcessName;
    public string MatchValue { get; set; } = "";
    public int Percent { get; set; }

    // Профиль всегда применяется только к тому монитору, на котором СЕЙЧАС находится
    // окно совпавшего приложения (определяется динамически, см. AppProfileEngine) —
    // поэтому здесь намеренно нет списка мониторов, в отличие от ScheduleRule.
    public bool Matches(string processName, string windowTitle)
    {
        var normalizedValue = MatchValue.Trim();
        if (normalizedValue.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalizedValue = normalizedValue[..^4];
        }

        return MatchType switch
        {
            AppMatchType.ProcessName => string.Equals(processName, normalizedValue, StringComparison.OrdinalIgnoreCase),
            AppMatchType.WindowTitle => windowTitle.Contains(normalizedValue, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}
