using Microsoft.Win32;

namespace BrightnessControl.Core;

// Единая точка входа для GUI/трея: агрегирует DDC/CI, WMI и оверлей-fallback
// провайдеры за одним API "по монитору" / "все сразу", следит за
// переподключением мониторов и запоминает последнее применённое значение.
public sealed class BrightnessController : IDisposable
{
    // Некоторым DDC/CI-мониторам ("смарт"-панели с фоновой прошивкой, см. FP1/FP2)
    // нужна пауза между последовательными записями, иначе они перестают отвечать.
    // Но одна и та же пауза для всех мониторов означает, что надёжные (Dell/Acer)
    // без нужды тормозятся из-за настроек, подобранных под самый капризный.
    // Поэтому пауза — персистентный профиль на монитор, который сам подстраивается:
    // растёт после медленной/неудачной записи, потихоньку падает после быстрой.
    private const int MinPacingMs = 80;
    private const int MaxPacingMs = 600;
    private const int PacingGrowStepMs = 100;
    private const int PacingRecoverStepMs = 25;

    private readonly DdcCiBrightnessProvider _ddcCi = new();
    private readonly WmiBrightnessProvider _wmi = new();
    private readonly OverlayFallbackBrightnessProvider _fallback = new();
    private readonly IBrightnessStateStore _store;
    private readonly IMonitorPacingStore _pacingStore;
    private IReadOnlyList<MonitorInfo> _monitors;
    private bool _disposed;

    public event Action<IReadOnlyList<MonitorInfo>>? MonitorsChanged;

    public BrightnessController(IBrightnessStateStore? store = null, IMonitorPacingStore? pacingStore = null)
    {
        _store = store ?? new JsonFileBrightnessStateStore();
        _pacingStore = pacingStore ?? new JsonFileMonitorPacingStore();
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
        bool ok;

        if (monitor.ConnectionKind == MonitorConnectionKind.ExternalDdcCi)
        {
            ok = _ddcCi.SetBrightness(monitor, percent, out var attemptsUsed);
            AdaptPacing(monitor, attemptsUsed, ok);
        }
        else
        {
            ok = monitor.ConnectionKind switch
            {
                MonitorConnectionKind.InternalPanel => _wmi.SetBrightness(monitor, percent),
                MonitorConnectionKind.Unsupported => _fallback.SetBrightness(monitor, percent),
                _ => false,
            };
        }

        if (ok)
        {
            _store.SetLastPercent(GetMonitorKey(monitor), percent);
        }

        return ok;
    }

    // Сколько миллисекунд стоит подождать перед СЛЕДУЮЩЕЙ записью этому монитору —
    // подобрано по истории его собственного поведения (см. AdaptPacing).
    public int GetPacingMs(MonitorInfo monitor) =>
        Math.Clamp(_pacingStore.GetPacingMs(GetMonitorKey(monitor)), MinPacingMs, MaxPacingMs);

    // Пауза для операции "все сразу" — по самому требовательному из подключённых
    // DDC/CI-мониторов, чтобы не спровоцировать проблемы у самого капризного.
    public int GetGlobalPacingMs()
    {
        var ddcCiMonitors = _monitors.Where(m => m.ConnectionKind == MonitorConnectionKind.ExternalDdcCi).ToList();
        return ddcCiMonitors.Count == 0 ? MinPacingMs : ddcCiMonitors.Max(GetPacingMs);
    }

    private void AdaptPacing(MonitorInfo monitor, int attemptsUsed, bool succeeded)
    {
        var key = GetMonitorKey(monitor);
        var current = _pacingStore.GetPacingMs(key);

        // Ровно 1 попытка = монитор применил команду с первого раза, можно постепенно
        // ускоряться. Любые повторы или итоговая неудача = монитору нужно больше
        // "воздуха" между записями.
        var next = !succeeded || attemptsUsed > 1
            ? Math.Min(MaxPacingMs, current + PacingGrowStepMs)
            : Math.Max(MinPacingMs, current - PacingRecoverStepMs);

        if (next != current)
        {
            _pacingStore.SetPacingMs(key, next);
        }
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

    // Публично — нужен и другим потребителям Core (например, ScheduleEngine),
    // которым требуется тот же стабильный ключ монитора между запусками.
    public static string GetMonitorKey(MonitorInfo monitor) => DeviceIdentity.ExtractHardwareId(monitor.DeviceId) ?? monitor.DeviceId;

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
