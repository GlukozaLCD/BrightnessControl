namespace BrightnessControl.Core;

public sealed class IdleSettings
{
    public bool IsEnabled { get; set; } = true;
    public int IdleTimeoutMinutes { get; set; } = 5;
    public int DimPercent { get; set; } = 10;

    // Как часто IdleEngine опрашивает GetLastInputInfo. Дешёвая проверка, поэтому
    // маленький интервал по умолчанию — иначе восстановление после простоя
    // ощущается с заметной случайной задержкой до 1 интервала (было замечено
    // на 5-секундном интервале при обкатке FP6).
    public int PollIntervalSeconds { get; set; } = 1;
}
