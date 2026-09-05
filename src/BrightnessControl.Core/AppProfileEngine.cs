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

    private string? _activeProfileId;
    private string? _activeMonitorAdapterName;
    private int? _snapshotPercent;
    private bool _disposed;

    public AppProfileEngine(BrightnessController controller, IAppProfileStore? store = null, IForegroundAppWatcher? watcher = null)
    {
        _controller = controller;
        _store = store ?? new JsonFileAppProfileStore();
        _watcher = watcher ?? new ForegroundAppWatcher();
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
        if (_activeMonitorAdapterName is null || _snapshotPercent is null)
        {
            return;
        }

        var monitor = _controller.Monitors.FirstOrDefault(m => m.AdapterDeviceName == _activeMonitorAdapterName);
        if (monitor is not null)
        {
            _controller.SetBrightness(monitor, _snapshotPercent.Value);
        }

        _snapshotPercent = null;
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
