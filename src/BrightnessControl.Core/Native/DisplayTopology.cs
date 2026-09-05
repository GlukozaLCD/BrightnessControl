using System.Runtime.InteropServices;

namespace BrightnessControl.Core.Native;

internal static class DisplayTopology
{
    public static IEnumerable<User32.DISPLAY_DEVICE> EnumerateAttachedAdapters()
    {
        uint index = 0;
        while (true)
        {
            var device = new User32.DISPLAY_DEVICE { cb = Marshal.SizeOf<User32.DISPLAY_DEVICE>() };
            if (!User32.EnumDisplayDevices(null, index, ref device, 0))
            {
                yield break;
            }

            if ((device.StateFlags & User32.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
            {
                yield return device;
            }

            index++;
        }
    }

    public static IEnumerable<User32.DISPLAY_DEVICE> EnumerateActiveMonitorDevices(string adapterDeviceName)
    {
        uint index = 0;
        while (true)
        {
            var device = new User32.DISPLAY_DEVICE { cb = Marshal.SizeOf<User32.DISPLAY_DEVICE>() };
            if (!User32.EnumDisplayDevices(adapterDeviceName, index, ref device, User32.EDD_GET_DEVICE_INTERFACE_NAME))
            {
                yield break;
            }

            if ((device.StateFlags & User32.DISPLAY_DEVICE_ACTIVE) != 0)
            {
                yield return device;
            }

            index++;
        }
    }

    public static IntPtr FindMonitorHandle(string adapterDeviceName)
    {
        return FindMonitorHandle(adapterDeviceName, out _);
    }

    public static IntPtr FindMonitorHandle(string adapterDeviceName, out User32.RECT bounds)
    {
        var found = IntPtr.Zero;
        var foundBounds = default(User32.RECT);

        bool Callback(IntPtr hMonitor, IntPtr hdcMonitor, ref User32.RECT lprcMonitor, IntPtr dwData)
        {
            var info = new User32.MONITORINFOEX { cbSize = Marshal.SizeOf<User32.MONITORINFOEX>() };
            if (User32.GetMonitorInfo(hMonitor, ref info) && string.Equals(info.szDevice, adapterDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                found = hMonitor;
                foundBounds = info.rcMonitor;
                return false;
            }

            return true;
        }

        User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        bounds = foundBounds;
        return found;
    }

    // Обратная задача к FindMonitorHandle: по HMONITOR (например, от MonitorFromWindow)
    // получить имя адаптера ("\\.\DISPLAYn"), чтобы сопоставить его с MonitorInfo.
    public static string? GetAdapterDeviceName(IntPtr hMonitor)
    {
        var info = new User32.MONITORINFOEX { cbSize = Marshal.SizeOf<User32.MONITORINFOEX>() };
        return User32.GetMonitorInfo(hMonitor, ref info) ? info.szDevice : null;
    }

    public static Dxva2.PHYSICAL_MONITOR[] GetPhysicalMonitors(IntPtr hMonitor)
    {
        if (!Dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) || count == 0)
        {
            return Array.Empty<Dxva2.PHYSICAL_MONITOR>();
        }

        var monitors = new Dxva2.PHYSICAL_MONITOR[count];
        return Dxva2.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors)
            ? monitors
            : Array.Empty<Dxva2.PHYSICAL_MONITOR>();
    }
}
