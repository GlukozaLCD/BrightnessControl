using BrightnessControl.Core;

namespace BrightnessControl.App.Services;

// FP14 — "замочек" на яркость монитора: пока монитор залочен, автоматика
// (расписание/профиль приложения/простой — см. предикаты isMonitorLocked,
// которыми теперь оборудованы все три движка в Core) полностью его
// игнорирует. Ручное управление слайдером НЕ блокируется — лок защищает
// значение только от АВТОМАТИКИ, не от самого пользователя.
//
// Сервис намеренно "тупой" — только персистентность + событие. Логика "что
// применить монитору сразу после снятия лока" (приоритет простой → профиль →
// расписание) живёт в App.axaml.cs, единственном месте, которое уже знает про
// все три движка разом — размазывать эту логику по сервису не нужно.
public sealed class MonitorLockService
{
    private readonly MonitorLockStore _store;
    private readonly HashSet<string> _lockedKeys;

    public event Action<MonitorInfo, bool>? LockChanged;

    public MonitorLockService(MonitorLockStore? store = null)
    {
        _store = store ?? new MonitorLockStore();
        _lockedKeys = _store.Load();
    }

    public bool IsLocked(MonitorInfo monitor) => _lockedKeys.Contains(BrightnessController.GetMonitorKey(monitor));

    public void SetLocked(MonitorInfo monitor, bool locked)
    {
        var key = BrightnessController.GetMonitorKey(monitor);
        var changed = locked ? _lockedKeys.Add(key) : _lockedKeys.Remove(key);
        if (!changed)
        {
            return;
        }

        _store.Save(_lockedKeys);
        LockChanged?.Invoke(monitor, locked);
    }
}
