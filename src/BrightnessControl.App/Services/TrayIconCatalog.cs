namespace BrightnessControl.App.Services;

// Каталог доступных ФОРМ иконки трея (цвет и масштаб — отдельные параметры,
// см. TrayIconRenderer и TraySettings) — растёт по ходу FP8 по мере отбора
// удачных дизайнов из очередных раундов.
public sealed record TrayIconDesignOption(TrayIconDesign Design, string DisplayName);

public static class TrayIconCatalog
{
    public static readonly IReadOnlyList<TrayIconDesignOption> Designs =
    [
        new(TrayIconDesign.Spokes, "Спицы"),
        new(TrayIconDesign.DotRays, "Точки"),
        new(TrayIconDesign.ThinRays, "Лучи"),
        new(TrayIconDesign.TwinHorizon, "Отражение"),
        new(TrayIconDesign.DotSunrise, "Рассвет"),
        new(TrayIconDesign.TwinHorizonCapsule, "Восход"),
    ];
}
