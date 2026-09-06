namespace BrightnessControl.Core;

// Применяет профиль яркости ТОЛЬКО к монитору, на котором сейчас физически
// находится окно совпавшего приложения (не ко всем мониторам и не по заранее
// заданному списку — см. FP5: это само решает проблему мигания яркости на
// экранах, где ничего не менялось, и позволяет применять изменение мгновенно,
// без дебounce). При уходе с профильного приложения возвращает яркость этого
// монитора к значению, которое было ДО применения профиля.
public sealed class AppProfileEngine : IDisposable
{
    private readonly BrightnessController _controller;
    private readonly IAppProfileStore _store;
    private readonly IForegroundAppWatcher _watcher;
    private readonly IScheduleStore _scheduleStore;

    private string? _activeProfileId;
    private string? _activeMonitorAdapterName;
    private int? _snapshotPercent;
    private bool _disposed;

    // Для живой индикации "сейчас активно" в GUI (см. FP5 Фаза 2), а также чтобы
    // ScheduleEngine (FP4) мог узнать, какой монитор сейчас занят профилем, и не
    // перебивать его своими правилами — профиль приоритетнее расписания (решено
    // при планировании Фазы 3).
    public string? ActiveProfileId => _activeProfileId;
    public string? ActiveMonitorAdapterDeviceName => _activeMonitorAdapterName;

    public AppProfileEngine(
        BrightnessController controller,
        IAppProfileStore? store = null,
        IForegroundAppWatcher? watcher = null,
        IScheduleStore? scheduleStore = null)
    {
        _controller = controller;
        _store = store ?? new JsonFileAppProfileStore();
        _watcher = watcher ?? new ForegroundAppWatcher();
        _scheduleStore = scheduleStore ?? new JsonFileScheduleStore();
        _watcher.ForegroundChanged += OnForegroundChanged;
        _watcher.ReportCurrentForegroundWindow();
    }

    // Публично и принимает данные явно — чтобы можно было проверить логику
    // сопоставления/снимка/восстановления без реального нативного хука (тесты).
    public void OnForegroundChanged(ForegroundAppInfo info)
    {
        var settings = _store.Load();
        if (!settings.IsEnabled)
        {
            return;
        }

        var matched = settings.Profiles.FirstOrDefault(p => p.Matches(info.ProcessName, info.WindowTitle));

        // Тот же профиль на том же мониторе, что и на прошлом событии — не переприменяем.
        if (matched?.Id == _activeProfileId && info.MonitorAdapterDeviceName == _activeMonitorAdapterName)
        {
            return;
        }

        RestorePrevious();

        if (matched is not null && info.MonitorAdapterDeviceName is not null)
        {
            var monitor = _controller.Monitors.FirstOrDefault(m => m.AdapterDeviceName == info.MonitorAdapterDeviceName);
            if (monitor is not null)
            {
                _snapshotPercent = _controller.GetBrightness(monitor)?.Percent;
                _controller.SetBrightness(monitor, matched.Percent);
                _activeProfileId = matched.Id;
                _activeMonitorAdapterName = info.MonitorAdapterDeviceName;
                return;
            }
        }

        _activeProfileId = null;
        _activeMonitorAdapterName = null;
    }

    private void RestorePrevious()
    {
        if (_activeMonitorAdapterName is null)
        {
            return;
        }

        var monitor = _controller.Monitors.FirstOrDefault(m => m.AdapterDeviceName == _activeMonitorAdapterName);
        if (monitor is not null)
        {
            // Снимок мог устареть, если расписание успело смениться, пока профиль был
            // активен — поэтому вместо слепого отката к снимку сначала пересчитываем,
            // что расписание хочет ПРЯМО СЕЙЧАС для этого монитора, и используем снимок
            // только как запасной вариант (расписание выключено/не применимо к монитору).
            var restoreValue = ComputeScheduleFallback(monitor) ?? _snapshotPercent;
            if (restoreValue is not null)
            {
                _controller.SetBrightness(monitor, restoreValue.Value);
            }
        }

        _snapshotPercent = null;
    }

    private int? ComputeScheduleFallback(MonitorInfo monitor)
    {
        var schedule = _scheduleStore.Load();
        if (!schedule.IsEnabled || schedule.Rules.Count == 0)
        {
            return null;
        }

        var monitorKey = BrightnessController.GetMonitorKey(monitor);
        var applicableRules = schedule.Rules
            .Where(r => r.MonitorKeys.Count == 0 || r.MonitorKeys.Contains(monitorKey))
            .OrderBy(r => r.Time)
            .ToList();

        if (applicableRules.Count == 0)
        {
            return null;
        }

        return ScheduleEngine.FindActiveRule(applicableRules, TimeOnly.FromDateTime(DateTime.Now)).Percent;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.ForegroundChanged -= OnForegroundChanged;
        _watcher.Dispose();
    }
}
