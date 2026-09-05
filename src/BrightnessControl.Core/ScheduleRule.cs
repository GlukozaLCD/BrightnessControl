namespace BrightnessControl.Core;

public sealed class ScheduleRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public TimeOnly Time { get; set; }
    public int Percent { get; set; }

    // Пусто = правило действует на все подключённые мониторы; иначе — только на
    // перечисленные (ключи — как в BrightnessController.GetMonitorKey).
    public List<string> MonitorKeys { get; set; } = new();
}
