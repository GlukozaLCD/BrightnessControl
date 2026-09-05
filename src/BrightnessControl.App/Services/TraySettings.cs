namespace BrightnessControl.App.Services;

public sealed class TraySettings
{
    public int ScrollStepPercent { get; set; } = 10;
    public bool IsScrollEnabled { get; set; } = true;

    // "Липкие" значения при скролле над треем: при прохождении рядом с одним из
    // них скролл на нём один "тик" задерживается, а не проскакивает мимо — но
    // любые другие проценты остаются доступны как обычно, без ограничений.
    public List<int> StickyValues { get; set; } = new();
}
