using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using BrightnessControl.App.Native;
using Microsoft.Win32;

namespace BrightnessControl.App.Services;

// FP9 Фаза 7: подкрашивает акцент Fluent-темы Avalonia в реальный акцентный цвет
// Windows, а не в зашитый по умолчанию синий — чтобы приложение визуально
// "вливалось" в систему пользователя.
//
// ВАЖНО: правильный способ — не подмена плоских ресурсов ("SystemAccentColor" и
// т.п. в Application.Resources, так пробовали раньше — эффекта не было), а прямая
// установка FluentTheme.Palettes[variant].Accent. Avalonia сама выводит из этого
// одного значения все 6 производных (Light1-3/Dark1-3) — вручную их вычислять не
// нужно. Слушает SystemEvents.UserPreferenceChanged для живого обновления без
// перезапуска — тот же механизм, что уже используется в BrightnessController для
// реакции на переподключение мониторов, только другая категория события.
public sealed class AccentColorService : IDisposable
{
    private readonly AppSettings _appSettings;
    private readonly AppSettingsStore _appSettingsStore;
    private Color? _originalLightAccent;
    private Color? _originalDarkAccent;
    private bool _disposed;

    public AccentColorService(AppSettings appSettings, AppSettingsStore appSettingsStore)
    {
        _appSettings = appSettings;
        _appSettingsStore = appSettingsStore;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Apply();
    }

    public void SetEnabled(bool enabled)
    {
        _appSettings.UseWindowsAccentColor = enabled;
        _appSettingsStore.Save(_appSettings);
        Apply();
    }

    public void SetCustomAccentColor(string hex)
    {
        _appSettings.CustomAccentColorHex = hex;
        _appSettingsStore.Save(_appSettings);
        Apply();
    }

    public void SetColorHarmonyScheme(ColorHarmonyScheme scheme)
    {
        _appSettings.ColorHarmonySchemeId = scheme.ToString();
        _appSettingsStore.Save(_appSettings);
        Apply();
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    public void Apply()
    {
        FluentTheme? fluentTheme = null;
        if (Application.Current?.Styles is { } styles)
        {
            foreach (var style in styles)
            {
                if (style is FluentTheme theme)
                {
                    fluentTheme = theme;
                    break;
                }
            }
        }

        if (fluentTheme is null)
        {
            return;
        }

        var lightPalette = GetOrCreatePalette(fluentTheme, ThemeVariant.Light);
        var darkPalette = GetOrCreatePalette(fluentTheme, ThemeVariant.Dark);

        // Запоминаем оригинальный акцент только один раз, до первой подмены —
        // нужен, чтобы честно откатиться назад при выключении переключателя.
        _originalLightAccent ??= lightPalette.Accent;
        _originalDarkAccent ??= darkPalette.Accent;

        Color primary;

        // Честная тема важнее вливания в систему в режиме высокой контрастности —
        // не форсируем акцент поверх настроек accessibility пользователя. В этой
        // ветке (и когда переключатель выключен) раньше AppAccentBrush красился
        // в чёрный (Colors.Black) — на тёмном фоне это делало акцентные элементы
        // (полоска заголовка, рамка активного пункта списка) фактически
        // невидимыми. FP13: используем свой акцентный цвет вместо чёрного.
        if (!_appSettings.UseWindowsAccentColor || IsHighContrastActive() || !TryGetWindowsAccentColor(out var accent))
        {
            lightPalette.Accent = _originalLightAccent.Value;
            darkPalette.Accent = _originalDarkAccent.Value;
            primary = Color.TryParse(_appSettings.CustomAccentColorHex, out var custom) ? custom : Color.FromRgb(0x00, 0x78, 0xD4);
        }
        else
        {
            lightPalette.Accent = accent;
            darkPalette.Accent = accent;
            primary = accent;
        }

        ApplyAccentBrushes(primary);
    }

    // Ресурсы под собственными именами (не завязаны на точные ключи Fluent-темы,
    // в которых легко ошибиться) — используются явно там, где акцент должен быть
    // заметен сразу и без взаимодействия с контролами (например, рамка бокового
    // списка категорий в SettingsWindow), а не только на выделении/чекбоксах.
    //
    // AppAccentOnBrush (FP12) — контрастный цвет ТЕКСТА/ИКОНОК поверх акцентной
    // заливки (активный пункт навигации, залитая часть слайдера и т.п.). Акцент
    // теперь динамический (из Windows или свой — FP13), а не один зашитый
    // оттенок, как в дизайн-макете, поэтому контраст вычисляется по яркости
    // конкретного цвета, а не жёстко задан.
    //
    // AppAccentSecondaryBrush (FP13) — вычисляется из основного акцента по
    // выбранной пользователем схеме цветовой гармонии (ColorHarmony), с
    // подстройкой светлоты под активную тему. Используется в единственном
    // месте, где уже существует понятие "основной/дополнительный" — лучи
    // HUD-солнца (BrightnessHudWindow, _secondaryRays).
    private void ApplyAccentBrushes(Color primary)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var isDarkTheme = app.ActualThemeVariant == ThemeVariant.Dark;
        var scheme = Enum.TryParse<ColorHarmonyScheme>(_appSettings.ColorHarmonySchemeId, out var parsedScheme)
            ? parsedScheme
            : ColorHarmonyScheme.Complementary;
        var secondary = ColorHarmony.ComputeSecondary(primary, scheme, isDarkTheme);

        app.Resources["AppAccentBrush"] = new SolidColorBrush(primary);
        app.Resources["AppAccentOnBrush"] = new SolidColorBrush(GetContrastingTextColor(primary));
        app.Resources["AppAccentSecondaryBrush"] = new SolidColorBrush(secondary);
    }

    private static Color GetContrastingTextColor(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.6 ? Color.FromRgb(0x1B, 0x13, 0x18) : Colors.White;
    }

    private static ColorPaletteResources GetOrCreatePalette(FluentTheme theme, ThemeVariant variant)
    {
        if (theme.Palettes.TryGetValue(variant, out var existing) && existing is ColorPaletteResources palette)
        {
            return palette;
        }

        var created = new ColorPaletteResources();
        theme.Palettes[variant] = created;
        return created;
    }

    private static bool TryGetWindowsAccentColor(out Color color)
    {
        if (Dwmapi.DwmGetColorizationColor(out var raw, out _) != 0)
        {
            color = default;
            return false;
        }

        var r = (byte)((raw >> 16) & 0xFF);
        var g = (byte)((raw >> 8) & 0xFF);
        var b = (byte)(raw & 0xFF);
        color = new Color(255, r, g, b);
        return true;
    }

    private static bool IsHighContrastActive()
    {
        var info = new User32Native.HIGHCONTRAST { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<User32Native.HIGHCONTRAST>() };
        if (!User32Native.SystemParametersInfo(User32Native.SPI_GETHIGHCONTRAST, info.cbSize, ref info, 0))
        {
            return false;
        }

        return (info.dwFlags & User32Native.HCF_HIGHCONTRASTON) != 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
