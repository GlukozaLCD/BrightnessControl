using BrightnessControl.Core.Native;

namespace BrightnessControl.Core;

public sealed class DdcCiBrightnessProvider
{
    // Некоторые мониторы (замечено на "смарт"-мониторе с фоновой прошивкой,
    // подключённом по HDMI) подтверждают приём команды на шине DDC/CI
    // (SetMonitorBrightness возвращает true), но иногда молча её игнорируют.
    // Поэтому не верим ACK на шине — проверяем фактическое значение и
    // при расхождении перепосылаем команду ещё несколько раз.
    private const int MaxSetAttempts = 4;
    private const int SettleDelayMs = 350;

    public BrightnessLevel? GetBrightness(MonitorInfo monitor)
    {
        return WithPhysicalMonitorHandle<BrightnessLevel?>(monitor, handle =>
            Dxva2.GetMonitorBrightness(handle, out var min, out var current, out var max)
                ? new BrightnessLevel(min, current, max)
                : null);
    }

    public bool SetBrightness(MonitorInfo monitor, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);

        return WithPhysicalMonitorHandle(monitor, handle =>
        {
            if (!Dxva2.GetMonitorBrightness(handle, out var min, out _, out var max) || max <= min)
            {
                return false;
            }

            var raw = min + (uint)Math.Round((max - min) * (percent / 100.0));

            for (var attempt = 1; attempt <= MaxSetAttempts; attempt++)
            {
                if (!Dxva2.SetMonitorBrightness(handle, raw))
                {
                    continue;
                }

                Thread.Sleep(SettleDelayMs);

                if (Dxva2.GetMonitorBrightness(handle, out _, out var current, out _) && current == raw)
                {
                    return true;
                }
            }

            return false;
        });
    }

    private static T WithPhysicalMonitorHandle<T>(MonitorInfo monitor, Func<IntPtr, T> action)
    {
        var hMonitor = DisplayTopology.FindMonitorHandle(monitor.AdapterDeviceName);
        if (hMonitor == IntPtr.Zero)
        {
            return default!;
        }

        var devices = DisplayTopology.EnumerateActiveMonitorDevices(monitor.AdapterDeviceName).ToList();
        var index = devices.FindIndex(d => d.DeviceID == monitor.DeviceId);
        if (index < 0)
        {
            return default!;
        }

        var physicalMonitors = DisplayTopology.GetPhysicalMonitors(hMonitor);
        if (index >= physicalMonitors.Length)
        {
            return default!;
        }

        try
        {
            return action(physicalMonitors[index].hPhysicalMonitor);
        }
        finally
        {
            Dxva2.DestroyPhysicalMonitors((uint)physicalMonitors.Length, physicalMonitors);
        }
    }
}
