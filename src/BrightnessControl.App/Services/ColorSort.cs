namespace BrightnessControl.App.Services;

// Сортирует палитру цветов иконки трея по цветовому кругу, а не по порядку
// добавления — так список читается как понятный градиент оттенков.
public static class ColorSort
{
    public static List<string> SortByHue(IEnumerable<string> hexColors) =>
        hexColors
            .Select(hex => (Hex: hex, Hsl: ToHsl(System.Drawing.ColorTranslator.FromHtml(hex))))
            .OrderBy(t => t.Hsl.H)
            .ThenBy(t => t.Hsl.S)
            .ThenBy(t => t.Hsl.L)
            .Select(t => t.Hex)
            .ToList();

    private static (double H, double S, double L) ToHsl(System.Drawing.Color c)
    {
        var r = c.R / 255.0;
        var g = c.G / 255.0;
        var b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;

        if (max == min)
        {
            return (0, 0, l);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
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

        return (h * 60, s, l);
    }
}
