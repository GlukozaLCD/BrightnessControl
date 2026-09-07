namespace BrightnessControl.App.Services;

// FP13 — набор классических правил цветовой гармонии, доступных пользователю
// для выбора; строка (а не голый enum используется наружу — см.
// AppSettings.ColorHarmonySchemeId), по той же схеме расширяемости, что и
// TrayIconDesignId/HudStyleId.
public enum ColorHarmonyScheme
{
    Monochromatic,
    AnalogousClose,
    Analogous,
    AnalogousWide,
}

// Вычисляет ДОПОЛНИТЕЛЬНЫЕ цвета из основного акцента (см. AccentColorService)
// по правилам классической цветовой гармонии. Структура — три ПАРЫ,
// уточнённые пользователем явно (2026-09-07): "акцентный цвет, второй цвет
// под стать акценту (тот же цвет, но другой оттенок), противоположный
// основному цвет, второй противоположный (тот же цвет, другой оттенок),
// фон, второй цвет под стать акцентному фону (тот же цвет, другой
// оттенок)":
//   1. Accent (сам основной цвет — передаётся в ComputePalette, не
//      вычисляется здесь) + AccentVariant (та же H/S, другая светлота).
//   2. Opposite (сдвиг оттенка по выбранной схеме) + OppositeVariant (та же
//      H/S, что и Opposite, другая светлота).
//   3. AccentBackground (оттенок акцента, резко приглушённый по насыщенности
//      и сдвинутый в фоновый диапазон темы — едва заметный тинт, а не
//      яркий цвет) + AccentBackgroundVariant (второй тон того же тинта).
// Светлота везде подстраивается под активную тему (тёмная/светлая) — сдвиг
// оттенка сам по себе не гарантирует, что результат останется читаемым.
public static class ColorHarmony
{
    public readonly record struct AccentPalette(
        Avalonia.Media.Color AccentVariant,
        Avalonia.Media.Color Opposite,
        Avalonia.Media.Color OppositeVariant,
        Avalonia.Media.Color AccentBackground,
        Avalonia.Media.Color AccentBackgroundVariant);

    public static AccentPalette ComputePalette(Avalonia.Media.Color primary, ColorHarmonyScheme scheme, bool isDarkTheme)
    {
        var (h, s, l) = ToHsl(primary);

        // Большие сдвиги оттенка (было: Complementary 180°, Split-Complementary
        // 150°, Triadic 120°) на живом фидбеке пользователя (2026-09-07)
        // раз за разом давали кричащие, несочетаемые пары (зелёный/розовый и
        // т.п.) — подтверждено явно: "только большой сдвиг оттенка плох".
        // Все схемы теперь в "мягком" диапазоне 0–45°, отличаются только
        // ШИРИНОЙ сдвига — от Monochromatic (совсем без смены оттенка,
        // только насыщенность) до AnalogousWide (45°, самый заметный сдвиг
        // из оставшихся, но всё ещё намного мягче прежних 120-180°).
        var (oppositeHueOffset, oppositeSaturationMultiplier) = scheme switch
        {
            ColorHarmonyScheme.Monochromatic => (0.0, 0.45),
            ColorHarmonyScheme.AnalogousClose => (15.0, 1.0),
            ColorHarmonyScheme.Analogous => (30.0, 1.0),
            ColorHarmonyScheme.AnalogousWide => (45.0, 1.0),
            _ => (30.0, 1.0),
        };

        // Тот же принцип "второй оттенок того же цвета" применяется трижды
        // (к Accent, к Opposite и к фоновому тинту) — светлее в тёмной теме,
        // темнее в светлой, чтобы пара визуально читалась как "два тона
        // одного цвета", а не как случайные несвязанные оттенки.
        var variantDelta = isDarkTheme ? 0.16 : -0.16;

        var accentVariant = FromHsl(h, s, ClampReadable(l + variantDelta, isDarkTheme));

        var oppositeHue = (h + oppositeHueOffset) % 360.0;
        var oppositeSaturation = s * oppositeSaturationMultiplier;
        var oppositeLightness = ClampReadable(l, isDarkTheme);
        var opposite = FromHsl(oppositeHue, oppositeSaturation, oppositeLightness);
        var oppositeVariant = FromHsl(oppositeHue, oppositeSaturation, ClampReadable(oppositeLightness + variantDelta, isDarkTheme));

        // Фон "под стать акценту" — тот же оттенок, но насыщенность резко
        // приглушена и светлота сдвинута в фоновый диапазон темы (почти
        // незаметный тинт поверх обычного тёмного/светлого фона, а не
        // полноценный яркий цвет).
        var backgroundSaturation = s * 0.35;
        var backgroundLightness = isDarkTheme ? 0.16 : 0.94;
        var accentBackground = FromHsl(h, backgroundSaturation, backgroundLightness);
        var accentBackgroundVariant = FromHsl(h, backgroundSaturation, Math.Clamp(backgroundLightness + (isDarkTheme ? 0.05 : -0.05), 0.0, 1.0));

        return new AccentPalette(accentVariant, opposite, oppositeVariant, accentBackground, accentBackgroundVariant);
    }

    // Держит цвет достаточно контрастным относительно фона активной темы:
    // не слишком тёмным на тёмном фоне, не слишком светлым на светлом.
    private static double ClampReadable(double l, bool isDarkTheme) =>
        Math.Clamp(isDarkTheme ? Math.Max(l, 0.45) : Math.Min(l, 0.6), 0.0, 1.0);

    private static (double H, double S, double L) ToHsl(Avalonia.Media.Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2.0;

        if (max == min)
        {
            return (0, 0, l);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

        double h;
        if (max == r)
        {
            h = (g - b) / d + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = (b - r) / d + 2;
        }
        else
        {
            h = (r - g) / d + 4;
        }

        h *= 60.0;
        return (h, s, l);
    }

    private static Avalonia.Media.Color FromHsl(double h, double s, double l)
    {
        if (s == 0)
        {
            var gray = (byte)Math.Round(l * 255.0);
            return Avalonia.Media.Color.FromRgb(gray, gray, gray);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        var hk = h / 360.0;

        var r = HueToRgb(p, q, hk + 1.0 / 3.0);
        var g = HueToRgb(p, q, hk);
        var b = HueToRgb(p, q, hk - 1.0 / 3.0);

        return Avalonia.Media.Color.FromRgb(
            (byte)Math.Round(r * 255.0),
            (byte)Math.Round(g * 255.0),
            (byte)Math.Round(b * 255.0));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        if (t < 1.0 / 6.0)
        {
            return p + (q - p) * 6 * t;
        }

        if (t < 1.0 / 2.0)
        {
            return q;
        }

        if (t < 2.0 / 3.0)
        {
            return p + (q - p) * (2.0 / 3.0 - t) * 6;
        }

        return p;
    }
}
