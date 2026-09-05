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

    // Шаг прилипания слайдеров в окне настроек — отдельно от шага скролла над
    // иконкой трея (TraySettings.ScrollStepPercent), это разные органы управления.
    public int SliderStepPercent { get; set; } = 5;
}
