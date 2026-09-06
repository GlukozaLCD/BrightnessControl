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

    // FP9 Фаза 7: подкрашивать акцентные элементы Fluent-темы в цвет, который
    // пользователь выбрал в Параметры Windows → Персонализация → Цвета, а не в
    // зашитый по умолчанию синий Avalonia — приложение должно "вливаться" в систему.
    public bool UseWindowsAccentColor { get; set; } = true;
}
