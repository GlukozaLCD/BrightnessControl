namespace BrightnessControl.App.Services;

// Запись яркости по DDC/CI может занимать до пары секунд (повторы для капризных
// мониторов, см. FP1) — если делать это в UI-потоке на каждое изменение слайдера
// или "тик" колеса, приложение подвисает. Этот класс уносит запись в фон и
// схлопывает быстрые повторные запросы в одно применение последнего значения,
// вместо очереди из них одна за другой. Используется и для трея (FP2), и для
// слайдеров в окне настроек (FP3) — по одному инстансу на независимый "канал"
// записи (глобальный или на конкретный монитор).
//
// Пауза между применениями не фиксированная — берётся из
// BrightnessController.GetPacingMs/GetGlobalPacingMs, который сам подбирает её
// по истории поведения конкретного монитора (см. Core/BrightnessController.cs).
public sealed class CoalescingBrightnessApplier
{
    private readonly object _lock = new();
    private readonly Action<int> _apply;
    private readonly Func<int> _getPacingMs;
    private int? _pending;
    private bool _running;

    public CoalescingBrightnessApplier(Action<int> apply, Func<int> getPacingMs)
    {
        _apply = apply;
        _getPacingMs = getPacingMs;
    }

    public void Request(int percent)
    {
        lock (_lock)
        {
            _pending = percent;
            if (_running)
            {
                return;
            }

            _running = true;
        }

        ThreadPool.QueueUserWorkItem(_ => Loop());
    }

    private void Loop()
    {
        while (true)
        {
            int value;
            lock (_lock)
            {
                if (_pending is not { } pending)
                {
                    _running = false;
                    return;
                }

                value = pending;
                _pending = null;
            }

            _apply(value);
            Thread.Sleep(_getPacingMs());
        }
    }
}
