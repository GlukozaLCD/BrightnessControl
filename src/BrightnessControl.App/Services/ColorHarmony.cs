namespace BrightnessControl.App.Services;

// FP13 — набор классических правил цветовой гармонии, доступных пользователю
// для выбора; строка (а не голый enum используется наружу — см.
// AppSettings.ColorHarmonySchemeId), по той же схеме расширяемости, что и
// TrayIconDesignId/HudStyleId.
public enum ColorHarmonyScheme
{
    Complementary,
    Analogous,
    Triadic,
}

// Вычисляет ВТОРИЧНЫЙ акцентный цвет из основного (см. AccentColorService) по
// правилам классической цветовой гармонии — сдвиг оттенка (Hue) в HSL на
// фиксированный угол, зависящий от выбранной схемы. Светлота дополнительно
// подстраивается под активную тему (тёмная/светлая) — сдвиг оттенка сам по
// себе не гарантирует, что результат останется читаемым на конкретном фоне
// (например, тёмно-синий вторичный цвет на тёмном фоне почти не виден).
public static class ColorHarmony
{
    public static Avalonia.Media.Color ComputeSecondary(Avalonia.Media.Color primary, ColorHarmonyScheme scheme, bool isDarkTheme)
    {
        var (h, s, l) = ToHsl(primary);

        var hueShiftDegrees = scheme switch
        {
            ColorHarmonyScheme.Complementary => 180.0,
            ColorHarmonyScheme.Analogous => 30.0,
            ColorHarmonyScheme.Triadic => 120.0,
            _ => 180.0,
        };

        var newHue = (h + hueShiftDegrees) % 360.0;

        // Тёмная тема — вторичный цвет должен остаться достаточно светлым,
        // чтобы читаться на тёмном фоне; светлая тема — наоборот, не должен
        // "выбеливаться" на светлом фоне.
        var newLightness = isDarkTheme ? Math.Max(l, 0.55) : Math.Min(l, 0.5);

        return FromHsl(newHue, s, newLightness);
    }

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
