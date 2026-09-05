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

    public bool SetBrightness(MonitorInfo monitor, int percent) => SetBrightness(monitor, percent, out _);

    // attemptsUsed — точный сигнал "капризности" монитора на этой конкретной записи:
    // 1 = применилось с первого раза, >1 = потребовались повторы. Используется
    // BrightnessController для адаптивной паузы вместо оценки по времени (тайминги
    // подвержены системному джиттеру и близки к порогу даже у здоровых мониторов).
    public bool SetBrightness(MonitorInfo monitor, int percent, out int attemptsUsed)
    {
        percent = Math.Clamp(percent, 0, 100);
        var label = $"{monitor.FriendlyName} [{monitor.AdapterDeviceName}]";

        var result = WithPhysicalMonitorHandle(monitor, handle =>
        {
            if (!Dxva2.GetMonitorBrightness(handle, out var min, out var before, out var max) || max <= min)
            {
                DebugLog.Write($"SetBrightness {label} -> {percent}%: не удалось прочитать диапазон, отмена");
                return (Success: false, Attempts: 0);
            }

            var raw = min + (uint)Math.Round((max - min) * (percent / 100.0));
            DebugLog.Write($"SetBrightness {label} -> {percent}% (raw {raw}, было {before}, диапазон [{min};{max}])");

            for (var attempt = 1; attempt <= MaxSetAttempts; attempt++)
            {
                var ack = Dxva2.SetMonitorBrightness(handle, raw);
                DebugLog.Write($"  попытка {attempt}/{MaxSetAttempts}: SetMonitorBrightness -> ack={ack}");

                if (!ack)
                {
                    continue;
                }

                Thread.Sleep(SettleDelayMs);

                var readOk = Dxva2.GetMonitorBrightness(handle, out _, out var current, out _);
                DebugLog.Write($"  попытка {attempt}/{MaxSetAttempts}: проверка -> readOk={readOk}, current={current}, ожидалось={raw}");

                if (readOk && current == raw)
                {
                    DebugLog.Write($"  {label}: подтверждено на попытке {attempt}");
                    return (Success: true, Attempts: attempt);
                }
            }

            DebugLog.Write($"  {label}: НЕ подтверждено после {MaxSetAttempts} попыток");
            return (Success: false, Attempts: MaxSetAttempts);
        });

        attemptsUsed = result.Attempts;
        return result.Success;
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
