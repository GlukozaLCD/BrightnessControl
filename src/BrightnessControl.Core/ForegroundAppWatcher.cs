using System.Diagnostics;
using System.Text;
using BrightnessControl.Core.Native;

namespace BrightnessControl.Core;

// Отслеживает смену переднего окна через нативный WinEvent-хук
// (EVENT_SYSTEM_FOREGROUND) — в реальном времени, без опроса по таймеру.
// Хуку нужен поток со своим message loop, поэтому он живёт по тому же
// принципу, что и OverlayDimmer (FP1): отдельный STA-поток с GetMessage-циклом.
public sealed class ForegroundAppWatcher : IForegroundAppWatcher
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ForegroundNative.WinEventProc _winEventProcDelegate;
    private volatile uint _threadId;
    private IntPtr _hook;
    private bool _disposed;

    public event Action<ForegroundAppInfo>? ForegroundChanged;

    public ForegroundAppWatcher()
    {
        _winEventProcDelegate = OnWinEvent;

        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "BrightnessControl.ForegroundAppWatcher",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_threadId != 0)
        {
            OverlayNative.PostThreadMessage(_threadId, OverlayNative.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        _thread.Join();
        _ready.Dispose();
    }

    private void RunMessageLoop()
    {
        _threadId = OverlayNative.GetCurrentThreadId();

        _hook = ForegroundNative.SetWinEventHook(
            ForegroundNative.EVENT_SYSTEM_FOREGROUND, ForegroundNative.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProcDelegate, 0, 0, ForegroundNative.WINEVENT_OUTOFCONTEXT);

        _ready.Set();

        while (OverlayNative.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            OverlayNative.TranslateMessage(ref msg);
            OverlayNative.DispatchMessage(ref msg);
        }

        if (_hook != IntPtr.Zero)
        {
            ForegroundNative.UnhookWinEvent(_hook);
        }
    }

    // Сообщает о текущем переднем окне без ожидания следующей смены — иначе
    // движок профилей "не увидит" уже открытое профильное приложение. Вызывающий
    // код должен вызывать это ПОСЛЕ подписки на ForegroundChanged (иначе первый
    // отчёт улетит без подписчиков — конструктор возвращает управление сразу
    // после установки хука, не дожидаясь внешней подписки). Сами Win32-вызовы
    // здесь не привязаны к конкретному потоку, поэтому безопасно вызывать из
    // потока, откуда вызывался конструктор.
    public void ReportCurrentForegroundWindow()
    {
        ReportForegroundWindow(ForegroundNative.GetForegroundWindow());
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (eventType == ForegroundNative.EVENT_SYSTEM_FOREGROUND)
        {
            ReportForegroundWindow(hwnd);
        }
    }

    private void ReportForegroundWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var processName = GetProcessName(hwnd);
        if (processName is null)
        {
            return;
        }

        var windowTitle = GetWindowTitle(hwnd);
        var monitorHandle = ForegroundNative.MonitorFromWindow(hwnd, ForegroundNative.MONITOR_DEFAULTTONEAREST);
        var adapterDeviceName = monitorHandle == IntPtr.Zero ? null : DisplayTopology.GetAdapterDeviceName(monitorHandle);

        ForegroundChanged?.Invoke(new ForegroundAppInfo(processName, windowTitle, adapterDeviceName));
    }

    private static string? GetProcessName(IntPtr hwnd)
    {
        ForegroundNative.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Процесс уже завершился между событием и опросом — не критично, просто пропускаем.
            return null;
        }
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var buffer = new StringBuilder(512);
        return ForegroundNative.GetWindowText(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }
}
