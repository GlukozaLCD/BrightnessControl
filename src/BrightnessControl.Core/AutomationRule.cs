namespace BrightnessControl.Core;

// Правило конструктора автоматизации (FP10): максимум одно условие по времени и
// одно по процессу (не произвольный список условий — см. PLAN_FP10, Фаза 1, п.2).
// Хотя бы одно из двух должно быть задано, иначе правило никогда не сработает —
// это проверяется на уровне UI, а не здесь.
public sealed class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool IsEnabled { get; set; } = true;

    // Нормализованный список отрезков (см. TimeSegment.Normalize) — UI обязан
    // прогонять любое редактирование через него перед сохранением, поэтому
    // здесь предполагается, что список уже не содержит пересечений/касаний.
    // IsTimeAlwaysActive и непустой TimeSegments взаимоисключающи — см.
    // TimeSegment.Normalize и PLAN_FP10 Фаза 4.
    public List<TimeSegment> TimeSegments { get; set; } = new();
    public bool IsTimeAlwaysActive { get; set; }

    public AppMatchType ProcessMatchType { get; set; } = AppMatchType.ProcessName;
    public string? ProcessMatchValue { get; set; }

    // Имеет смысл, только когда заданы ОБА условия одновременно — иначе
    // игнорируется (единственное заданное условие решает всё само).
    public AutomationCombinator Combinator { get; set; } = AutomationCombinator.And;

    public int Percent { get; set; }

    // null = стандартный порядок приоритета (см. AutomationEngine.ResolveWinner);
    // задано — явный приоритет побеждает абсолютно (см. PLAN_FP10, Фаза 1, п.6).
    public int? Priority { get; set; }

    // Область действия, когда правило истинно ТОЛЬКО благодаря времени (при
    // отсутствии условия по процессу, либо когда сработало именно время в
    // комбинации ИЛИ). Пусто = все мониторы. Игнорируется, когда правило
    // истинно благодаря процессу — тогда область действия динамическая
    // (монитор, где сейчас окно), см. AutomationEngine.
    public List<string> MonitorKeys { get; set; } = new();

    public bool HasProcessCondition => !string.IsNullOrWhiteSpace(ProcessMatchValue);
    public bool HasTimeCondition => IsTimeAlwaysActive || TimeSegments.Count > 0;

    public bool MatchesTime(int minuteOfDay) => IsTimeAlwaysActive || TimeSegments.Any(s => s.Contains(minuteOfDay));

    public bool MatchesProcess(string processName, string windowTitle)
    {
        if (!HasProcessCondition)
        {
            return false;
        }

        var normalizedValue = ProcessMatchValue!.Trim();
        if (normalizedValue.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalizedValue = normalizedValue[..^4];
        }

        return ProcessMatchType switch
        {
            AppMatchType.ProcessName => string.Equals(processName, normalizedValue, StringComparison.OrdinalIgnoreCase),
            AppMatchType.WindowTitle => windowTitle.Contains(normalizedValue, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}
