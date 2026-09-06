namespace BrightnessControl.Core;

// Расписание "ступенчатое" на 24 часа: каждое правило действует от своего времени
// до времени следующего правила (по кругу через полночь). Ручная корректировка
// яркости (слайдером, скроллом трея) пока действует правило — не перебивается
// движком повторно, пока не наступит СЛЕДУЮЩАЯ точка расписания: движок помнит,
// какое правило было применено последним для каждого монитора, и переприменяет
// только при смене активного правила, а не на каждой периодической проверке.
public sealed class ScheduleEngine : IDisposable
{
    private readonly BrightnessController _controller;
    private readonly IScheduleStore _store;
    private readonly Func<MonitorInfo, bool>? _isMonitorSuppressed;
    private readonly Dictionary<string, string?> _lastActiveRuleId = new();
    private Timer? _timer;
    private bool _disposed;

    // isMonitorSuppressed: монитор, для которого сейчас возвращается true, полностью
    // пропускается на этом тике — используется AppProfileEngine (FP5), чтобы профиль
    // приложения был приоритетнее расписания, пока активен. lastActiveRuleId для
    // такого монитора НЕ обновляется, поэтому как только подавление снимется, движок
    // на следующем тике честно переприменит актуальное на тот момент правило (даже
    // если формально это то же правило, что действовало до подавления).
    public ScheduleEngine(
        BrightnessController controller,
        IScheduleStore? store = null,
        bool autoStart = true,
        Func<MonitorInfo, bool>? isMonitorSuppressed = null)
    {
        _controller = controller;
        _store = store ?? new JsonFileScheduleStore();
        _isMonitorSuppressed = isMonitorSuppressed;

        if (autoStart)
        {
            Start();
        }
    }

    public void Start()
    {
        var settings = _store.Load();
        var intervalMs = Math.Max(1, settings.CheckIntervalMinutes) * 60_000;

        _timer?.Dispose();
        _timer = new Timer(_ => EvaluateAndApply(DateTime.Now), null, 0, intervalMs);
    }

    // Публично и принимает время явно — чтобы можно было проверить логику без
    // ожидания реального таймера (см. тесты).
    public void EvaluateAndApply(DateTime now)
    {
        var settings = _store.Load();
        if (!settings.IsEnabled || settings.Rules.Count == 0)
        {
            return;
        }

        var timeOfDay = TimeOnly.FromDateTime(now);
        var orderedRules = settings.Rules.OrderBy(r => r.Time).ToList();

        foreach (var monitor in _controller.Monitors)
        {
            var monitorKey = BrightnessController.GetMonitorKey(monitor);
            var applicableRules = orderedRules
                .Where(r => r.MonitorKeys.Count == 0 || r.MonitorKeys.Contains(monitorKey))
                .ToList();

            if (applicableRules.Count == 0)
            {
                continue;
            }

            if (_isMonitorSuppressed?.Invoke(monitor) == true)
            {
                continue;
            }

            var activeRule = FindActiveRule(applicableRules, timeOfDay);
            _lastActiveRuleId.TryGetValue(monitorKey, out var lastId);

            if (lastId == activeRule.Id)
            {
                continue;
            }

            _controller.SetBrightness(monitor, activeRule.Percent);
            _lastActiveRuleId[monitorKey] = activeRule.Id;
        }
    }

    // Действующее правило — последнее по времени среди тех, чьё время уже
    // наступило сегодня; если таких нет (сейчас раньше самого раннего правила
    // суток), действует последнее правило ПРЕДЫДУЩЕГО дня — самое позднее по
    // времени в списке.
    public static ScheduleRule FindActiveRule(List<ScheduleRule> rulesSortedByTime, TimeOnly now)
    {
        ScheduleRule? candidate = null;
        foreach (var rule in rulesSortedByTime)
        {
            if (rule.Time <= now)
            {
                candidate = rule;
            }
        }

        return candidate ?? rulesSortedByTime[^1];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Dispose();
    }
}
