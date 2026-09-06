using System.Drawing;
using System.Runtime.InteropServices;
using BrightnessControl.App.Native;
using BrightnessControl.Core;

namespace BrightnessControl.App.Services;

// Иконка создаётся напрямую через Shell_NotifyIcon (а не Avalonia TrayIcon), потому что
// Windows не сообщает о скролле колеса мыши над треем через обычное сообщение обратного
// вызова иконки — это подтверждено на практике теми же приёмами, что использует, например,
// Twinkle Tray. Единственный надёжный способ — глобальный low-level хук мыши
// (WH_MOUSE_LL) + сверка координат курсора с прямоугольником СВОЕЙ иконки через
// Shell_NotifyIconGetRect, чтобы не реагировать на скролл над чужими иконками.
public readonly record struct TrayScrollEventArgs(int Notches, int CursorX, int CursorY);

public readonly record struct TrayClickEventArgs(int CursorX, int CursorY);

public sealed class TrayService : IDisposable
{
    private const uint IconId = 1;
    private const uint WM_TRAYICON = 0x8000 + 1; // WM_APP + 1
    private const uint WM_SET_ICON = 0x8000 + 2; // WM_APP + 2 — смена иконки "на лету" (см. SetIcon)

    // Класс окна регистрируется с уникальным именем на инстанс, чтобы его WndProc мог
    // напрямую указывать на метод ЭТОГО инстанса — тред-сообщения (скролл, правый клик)
    // обрабатываются конкретным TrayService, а не статическим общим обработчиком.
    private readonly string _className = $"BrightnessControlTrayWindow_{Guid.NewGuid():N}";

    private readonly string _tooltip;
    private readonly Icon _initialIcon;
    private readonly User32Native.WndProc _wndProcDelegate;
    private readonly User32Native.HookProc _hookProcDelegate;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);

    private IntPtr _hwnd;
    private IntPtr _hook;
    private IntPtr _hIcon;
    private Icon? _iconResource;
    private volatile uint _threadId;
    private bool _disposed;

    public event Action<TrayScrollEventArgs>? ScrollNotches;
    public event Action<TrayClickEventArgs>? RightClicked;
    public event Action<TrayClickEventArgs>? LeftClicked;

    public TrayService(Icon initialIcon, string tooltip)
    {
        _initialIcon = initialIcon;
        _tooltip = tooltip;
        _wndProcDelegate = WindowProc;
        _hookProcDelegate = HookProc;

        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "BrightnessControl.TrayService",
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
            User32Native.PostThreadMessage(_threadId, User32Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            User32Native.PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        }

        _thread.Join();
        _ready.Dispose();
    }

    private void RunMessageLoop()
    {
        _threadId = Kernel32Native.GetCurrentThreadId();

        var wndClass = new User32Native.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<User32Native.WNDCLASSEX>(),
            lpfnWndProc = _wndProcDelegate,
            lpszClassName = _className,
        };
        User32Native.RegisterClassEx(ref wndClass);

        _hwnd = User32Native.CreateWindowEx(0, _className, string.Empty, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        _iconResource = _initialIcon;
        _hIcon = _initialIcon.Handle;

        var data = new Shell32.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<Shell32.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = IconId,
            uFlags = Shell32.NIF_MESSAGE | Shell32.NIF_ICON | Shell32.NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = _tooltip,
        };

        Shell32.Shell_NotifyIcon(Shell32.NIM_ADD, ref data);

        _hook = User32Native.SetWindowsHookEx(User32Native.WH_MOUSE_LL, _hookProcDelegate, IntPtr.Zero, 0);

        _ready.Set();

        while (User32Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            User32Native.TranslateMessage(ref msg);
            User32Native.DispatchMessage(ref msg);
        }

        if (_hook != IntPtr.Zero)
        {
            User32Native.UnhookWindowsHookEx(_hook);
        }

        Shell32.Shell_NotifyIcon(Shell32.NIM_DELETE, ref data);
        User32Native.DestroyWindow(_hwnd);
        _iconResource?.Dispose();
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_SET_ICON)
        {
            ApplyNewIcon(lParam);
            return IntPtr.Zero;
        }

        if (msg == WM_TRAYICON)
        {
            var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg == User32Native.WM_RBUTTONUP || mouseMsg == User32Native.WM_CONTEXTMENU)
            {
                User32Native.GetCursorPos(out var pt);
                RightClicked?.Invoke(new TrayClickEventArgs(pt.X, pt.Y));
            }
            else if (mouseMsg == User32Native.WM_LBUTTONUP)
            {
                User32Native.GetCursorPos(out var pt);
                LeftClicked?.Invoke(new TrayClickEventArgs(pt.X, pt.Y));
            }

            return IntPtr.Zero;
        }

        return User32Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ApplyNewIcon(IntPtr lParam)
    {
        var handle = GCHandle.FromIntPtr(lParam);
        var newIcon = (Icon)handle.Target!;
        handle.Free();

        var data = new Shell32.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<Shell32.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = IconId,
            uFlags = Shell32.NIF_ICON,
            hIcon = newIcon.Handle,
        };
        Shell32.Shell_NotifyIcon(Shell32.NIM_MODIFY, ref data);

        var old = _iconResource;
        _iconResource = newIcon;
        _hIcon = newIcon.Handle;
        old?.Dispose();
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (uint)wParam.ToInt64() == User32Native.WM_MOUSEWHEEL)
        {
            var data = Marshal.PtrToStructure<User32Native.MSLLHOOKSTRUCT>(lParam);
            if (IsPointOverOurIcon(data.pt))
            {
                var delta = unchecked((short)(data.mouseData >> 16));
                var notches = delta / 120;
                if (notches != 0)
                {
                    ScrollNotches?.Invoke(new TrayScrollEventArgs(notches, data.pt.X, data.pt.Y));
                }

                return (IntPtr)1;
            }
        }

        return User32Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool IsPointOverOurIcon(User32Native.POINT pt)
    {
        if (!TryGetIconRect(out var rect))
        {
            return false;
        }

        return pt.X >= rect.X && pt.X < rect.X + rect.Width && pt.Y >= rect.Y && pt.Y < rect.Y + rect.Height;
    }

    // Живая смена иконки трея без пересоздания самого значка (FP8): иконка уже
    // отрисована вызывающим кодом (см. TrayIconRenderer) и передаётся STA-потоку
    // трея через PostMessage, т.к. там же создан _hwnd/_hIcon/_iconResource и
    // Shell_NotifyIcon(NIM_MODIFY) должен применяться рядом с остальной
    // обработкой сообщений этого окна, а не с постороннего потока. Managed-объект
    // Icon передаётся через GCHandle, т.к. Win32-сообщение способно нести только
    // указатель/число, а не ссылку на .NET-объект напрямую.
    public void SetIcon(Icon icon)
    {
        var handle = GCHandle.Alloc(icon);
        User32Native.PostMessage(_hwnd, WM_SET_ICON, IntPtr.Zero, GCHandle.ToIntPtr(handle));
    }

    // Нужен вызывающему коду (App.axaml.cs), чтобы прицепить поповер глобального
    // слайдера точно к иконке трея (FP9 Фаза 2) — не по центру монитора, как окно
    // настроек. Возвращает дружелюбный MonitorBounds, а не сырой Shell32.RECT.
    public bool TryGetIconRect(out MonitorBounds rect)
    {
        var identifier = new Shell32.NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<Shell32.NOTIFYICONIDENTIFIER>(),
            hWnd = _hwnd,
            uID = IconId,
            guidItem = Guid.Empty,
        };

        if (Shell32.Shell_NotifyIconGetRect(ref identifier, out var native) != 0)
        {
            rect = default;
            return false;
        }

        rect = new MonitorBounds(native.Left, native.Top, native.Right - native.Left, native.Bottom - native.Top);
        return true;
    }
}
