using System.Runtime.InteropServices;
using BrightnessControl.Core.Native;

namespace BrightnessControl.Core;

// Полупрозрачное чёрное окно поверх одного монитора — программная имитация
// затемнения там, где аппаратный DDC/CI недоступен. Каждому оверлею нужен
// собственный поток с настоящим message loop (Win32-окна без него не живут).
public sealed class OverlayDimmer : IDisposable
{
    private const string ClassName = "BrightnessControlOverlayWindow";
    private static readonly object ClassLock = new();
    private static readonly OverlayNative.WndProc WndProcDelegate = DefWndProc;
    private static bool _classRegistered;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private volatile IntPtr _hwnd;
    private volatile uint _threadId;
    private bool _disposed;

    public OverlayDimmer(MonitorBounds bounds)
    {
        _thread = new Thread(() => RunMessageLoop(bounds))
        {
            IsBackground = true,
            Name = "BrightnessControl.OverlayDimmer"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public void SetDimPercent(int percent)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        percent = Math.Clamp(percent, 0, 100);
        var alpha = (byte)Math.Round(percent * 255.0 / 100.0);
        OverlayNative.SetLayeredWindowAttributes(_hwnd, 0, alpha, OverlayNative.LWA_ALPHA);
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

    private void RunMessageLoop(MonitorBounds bounds)
    {
        _threadId = OverlayNative.GetCurrentThreadId();
        EnsureClassRegistered();

        var hwnd = OverlayNative.CreateWindowEx(
            OverlayNative.WS_EX_LAYERED | OverlayNative.WS_EX_TRANSPARENT | OverlayNative.WS_EX_TOOLWINDOW | OverlayNative.WS_EX_NOACTIVATE,
            ClassName,
            string.Empty,
            OverlayNative.WS_POPUP,
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            _ready.Set();
            return;
        }

        OverlayNative.SetLayeredWindowAttributes(hwnd, 0, 0, OverlayNative.LWA_ALPHA);
        OverlayNative.ShowWindow(hwnd, OverlayNative.SW_SHOWNOACTIVATE);
        OverlayNative.SetWindowPos(hwnd, OverlayNative.HWND_TOPMOST, 0, 0, 0, 0,
            OverlayNative.SWP_NOMOVE | OverlayNative.SWP_NOSIZE | OverlayNative.SWP_NOACTIVATE);

        _hwnd = hwnd;
        _ready.Set();

        while (OverlayNative.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            OverlayNative.TranslateMessage(ref msg);
            OverlayNative.DispatchMessage(ref msg);
        }

        OverlayNative.DestroyWindow(hwnd);
    }

    private static void EnsureClassRegistered()
    {
        lock (ClassLock)
        {
            if (_classRegistered)
            {
                return;
            }

            var wndClass = new OverlayNative.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<OverlayNative.WNDCLASSEX>(),
                lpfnWndProc = WndProcDelegate,
                hbrBackground = Gdi32.CreateSolidBrush(0x00000000),
                lpszClassName = ClassName
            };

            OverlayNative.RegisterClassEx(ref wndClass);
            _classRegistered = true;
        }
    }

    private static IntPtr DefWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return OverlayNative.DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
