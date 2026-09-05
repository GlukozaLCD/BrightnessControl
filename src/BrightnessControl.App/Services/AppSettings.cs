namespace BrightnessControl.App.Services;

public enum AppThemePreference
{
    System,
    Light,
    Dark,
}

public sealed class AppSettings
{
    public AppThemePreference Theme { get; set; } = AppThemePreference.System;
}
