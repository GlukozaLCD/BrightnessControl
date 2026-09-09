namespace BrightnessControl.Core;

public sealed class AutomationSettings
{
    public bool IsEnabled { get; set; } = true;

    // Порядок в списке — это и порядок редактирования в UI, и тай-брейк
    // приоритета (см. AutomationEngine.ResolveWinner и PLAN_FP10, Фаза 1, п.6).
    public List<AutomationRule> Rules { get; set; } = new();

    public int CheckIntervalMinutes { get; set; } = 1;
}
