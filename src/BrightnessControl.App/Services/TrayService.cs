using System.Drawing;
using System.Runtime.InteropServices;
using BrightnessControl.App.Native;

namespace BrightnessControl.App.Services;

// Иконка создаётся напрямую через Shell_NotifyIcon (а не Avalonia TrayIcon), потому что
// Windows не сообщает о скролле колеса мыши над треем через обычное сообщение обратного
// вызова иконки — это подтверждено на практике теми же приёмами, что использует, например,
// Twinkle Tray. Единственный надёжный способ — глобальный low-level хук мыши
// (WH_MOUSE_LL) + сверка координат курсора с прямоугольником СВОЕЙ иконки через
// Shell_NotifyIconGetRect, чтобы не реагировать на скролл над чужими иконками.
public readonly record struct TrayScrollEventArgs(int Notches, int CursorX, int CursorY);

public readonly record struct TrayMenuClickEventArgs(uint Id, int CursorX, int CursorY);

public sealed class TrayService : IDisposable
{
    private const uint IconId = 1;
    private const uint WM_TRAYICON = 0x8000 + 1; // WM_APP + 1

    // Класс окна регистрируется с уникальным именем на инстанс, чтобы его WndProc мог
    // напрямую указывать на метод ЭТОГО инстанса — тред-сообщения (скролл, правый клик)
    // обрабатываются конкретным TrayService, а не статическим общим обработчиком.
    private readonly string _className = $"BrightnessControlTrayWindow_{Guid.NewGuid():N}";

    private readonly string _tooltip;
    private readonly Uri _iconUri;
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
    public event Action<TrayMenuClickEventArgs>? MenuItemClicked;

    // Меню строится заново при каждом открытии — вызывающая сторона решает, что в нём
    // должно быть (список мониторов может измениться между показами).
    public Func<IReadOnlyList<TrayMenuItem>>? BuildMenuItems { get; set; }

    public TrayService(Uri iconUri, string tooltip)
    {
        _iconUri = iconUri;
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

        _hIcon = LoadTrayIcon(_iconUri);

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
        if (msg == WM_TRAYICON)
        {
            var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMsg == User32Native.WM_RBUTTONUP || mouseMsg == User32Native.WM_CONTEXTMENU)
            {
                ShowContextMenu();
            }

            return IntPtr.Zero;
        }

        return User32Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        User32Native.GetCursorPos(out var pt);

        var items = BuildMenuItems?.Invoke() ?? Array.Empty<TrayMenuItem>();
        var submenus = new List<IntPtr>();
        var menu = BuildNativeMenu(items, submenus);

        User32Native.SetForegroundWindow(_hwnd);
        var cmd = User32Native.TrackPopupMenu(
            menu,
            User32Native.TPM_RETURNCMD | User32Native.TPM_NONOTIFY,
            pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        User32Native.PostMessage(_hwnd, User32Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);

        // Уничтожать нужно и корневое меню, и все вложенные подменю — DestroyMenu не
        // делает этого рекурсивно сам за нас для popup-подменю, добавленных как HMENU.
        foreach (var submenu in submenus)
        {
            User32Native.DestroyMenu(submenu);
        }

        User32Native.DestroyMenu(menu);

        if (cmd > 0)
        {
            MenuItemClicked?.Invoke(new TrayMenuClickEventArgs((uint)cmd, pt.X, pt.Y));
        }
    }

    private static IntPtr BuildNativeMenu(IReadOnlyList<TrayMenuItem> items, List<IntPtr> submenusAccumulator)
    {
        var menu = User32Native.CreatePopupMenu();

        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                User32Native.AppendMenu(menu, User32Native.MF_SEPARATOR, UIntPtr.Zero, string.Empty);
                continue;
            }

            if (item.SubItems is { Count: > 0 })
            {
                var submenu = BuildNativeMenu(item.SubItems, submenusAccumulator);
                submenusAccumulator.Add(submenu);
                User32Native.AppendMenu(menu, User32Native.MF_POPUP | User32Native.MF_STRING, (UIntPtr)submenu, item.Header);
                continue;
            }

            var flags = User32Native.MF_STRING | (item.IsEnabled ? 0 : User32Native.MF_GRAYED);
            User32Native.AppendMenu(menu, flags, (UIntPtr)(item.Id ?? 0), item.Header);
        }

        return menu;
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
        var identifier = new Shell32.NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<Shell32.NOTIFYICONIDENTIFIER>(),
            hWnd = _hwnd,
            uID = IconId,
            guidItem = Guid.Empty,
        };

        if (Shell32.Shell_NotifyIconGetRect(ref identifier, out var rect) != 0)
        {
            return false;
        }

        return pt.X >= rect.Left && pt.X < rect.Right && pt.Y >= rect.Top && pt.Y < rect.Bottom;
    }

    // Хэндл иконки должен жить, пока она зарегистрирована в трее — Icon.Dispose()
    // уничтожает и сам HICON, поэтому объект хранится как поле и освобождается только
    // после NIM_DELETE (см. конец RunMessageLoop), а не сразу после создания хэндла.
    private IntPtr LoadTrayIcon(Uri iconUri)
    {
        using var stream = Avalonia.Platform.AssetLoader.Open(iconUri);
        _iconResource = new Icon(stream);
        return _iconResource.Handle;
    }
}
