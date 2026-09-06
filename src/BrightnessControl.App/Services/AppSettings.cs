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

    // Процессы, скрытые пользователем из списка предложений при создании профиля
    // приложения (вкладка "Профили приложений") — там их слишком много без фильтра.
    public List<string> HiddenProcessNames { get; set; } = new();
}
