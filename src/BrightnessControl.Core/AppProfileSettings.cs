namespace BrightnessControl.Core;

public sealed class AppProfileSettings
{
    public bool IsEnabled { get; set; } = true;
    public List<AppProfile> Profiles { get; set; } = new();
}
