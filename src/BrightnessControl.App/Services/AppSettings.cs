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

    // FP13: свой акцентный цвет — используется вместо системного, когда
    // UseWindowsAccentColor выключен. Обычный Fluent-синий по умолчанию —
    // нейтральная отправная точка для пикера, ничего не навязывает.
    public string CustomAccentColorHex { get; set; } = "#0078D4";

    // FP13: правило вычисления ВТОРИЧНОГО акцентного цвета из основного (см.
    // ColorHarmony) — строка, а не голый enum, по той же схеме
    // расширяемости, что и TrayIconDesignId/HudStyleId. Действует всегда,
    // независимо от источника основного цвета (Windows или свой).
    public string ColorHarmonySchemeId { get; set; } = ColorHarmonyScheme.Analogous.ToString();

    // FP13: набор вычисленных цветов не меняется — меняется только, КУДА они
    // применяются. При включении пара "основной/тон 2" и пара
    // "противоположный/тон 2" меняются местами по всему приложению (то, что
    // раньше было "основным акцентом" на кнопках/навигации, становится
    // "противоположным" — сейчас видно только на доп. лучах HUD-солнца — и
    // наоборот).
    public bool SwapAccentRoles { get; set; }
}
