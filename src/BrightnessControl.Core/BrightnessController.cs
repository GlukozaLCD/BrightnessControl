using Microsoft.Win32;

namespace BrightnessControl.Core;

// Единая точка входа для GUI/трея: агрегирует DDC/CI, WMI и оверлей-fallback
// провайдеры за одним API "по монитору" / "все сразу", следит за
// переподключением мониторов и запоминает последнее применённое значение.
public sealed class BrightnessController : IDisposable
{
    private readonly DdcCiBrightnessProvider _ddcCi = new();
    private readonly WmiBrightnessProvider _wmi = new();
    private readonly OverlayFallbackBrightnessProvider _fallback = new();
    private readonly IBrightnessStateStore _store;
    private IReadOnlyList<MonitorInfo> _monitors;
    private bool _disposed;

    public event Action<IReadOnlyList<MonitorInfo>>? MonitorsChanged;

    public BrightnessController(IBrightnessStateStore? store = null)
    {
        _store = store ?? new JsonFileBrightnessStateStore();
        _monitors = MonitorEnumerator.EnumerateMonitors();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public IReadOnlyList<MonitorInfo> Monitors => _monitors;

    public BrightnessLevel? GetBrightness(MonitorInfo monitor) => monitor.ConnectionKind switch
    {
        MonitorConnectionKind.ExternalDdcCi => _ddcCi.GetBrightness(monitor),
        MonitorConnectionKind.InternalPanel => _wmi.GetBrightness(monitor),
        MonitorConnectionKind.Unsupported => _fallback.GetBrightness(monitor),
        _ => null,
    };

    public bool SetBrightness(MonitorInfo monitor, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);

        var ok = monitor.ConnectionKind switch
        {
            MonitorConnectionKind.ExternalDdcCi => _ddcCi.SetBrightness(monitor, percent),
            MonitorConnectionKind.InternalPanel => _wmi.SetBrightness(monitor, percent),
            MonitorConnectionKind.Unsupported => _fallback.SetBrightness(monitor, percent),
            _ => false,
        };

        if (ok)
        {
            _store.SetLastPercent(GetMonitorKey(monitor), percent);
        }

        return ok;
    }

    // Каждый монитор общается по DDC/CI через свой собственный физический канал
    // (свой кабель/порт), поэтому параллельная запись безопасна и ощущается
    // пользователем как настоящее "все сразу" — последовательный foreach давал
    // заметный разнобой по времени между мониторами, особенно с более медленными.
    public void SetAllBrightness(int percent)
    {
        var tasks = _monitors
            .Select(monitor => Task.Run(() => SetBrightness(monitor, percent)))
            .ToArray();

        Task.WaitAll(tasks);
    }

    public int? GetLastKnownPercent(MonitorInfo monitor) => _store.GetLastPercent(GetMonitorKey(monitor));

    private static string GetMonitorKey(MonitorInfo monitor) => DeviceIdentity.ExtractHardwareId(monitor.DeviceId) ?? monitor.DeviceId;

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _monitors = MonitorEnumerator.EnumerateMonitors();
        MonitorsChanged?.Invoke(_monitors);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _fallback.Dispose();
    }
}
