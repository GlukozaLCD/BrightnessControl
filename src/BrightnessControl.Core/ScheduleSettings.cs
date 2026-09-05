namespace BrightnessControl.Core;

public sealed class ScheduleSettings
{
    public bool IsEnabled { get; set; } = true;
    public List<ScheduleRule> Rules { get; set; } = new();

    // Как часто движок сверяется с часами, чтобы применить очередное правило.
    // Пока не редактируется через GUI (см. FP4), но вынесено как настоящая
    // настройка — чтобы позже подвязать интерфейс без переделки модели.
    public int CheckIntervalMinutes { get; set; } = 1;
}
