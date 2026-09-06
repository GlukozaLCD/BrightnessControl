using BrightnessControl.Core;

namespace BrightnessControl.App.Services;

// Windows нередко отдаёт одинаковое общее имя ("Generic PnP Monitor") для
// нескольких разных мониторов — без номера адаптера их не отличить ни в
// трей-меню, ни в окне настроек.
public static class MonitorLabel
{
    // Это имя ничего не говорит пользователю (одинаковое у всех "безымянных"
    // мониторов) — вместо "Generic PnP Monitor (DISPLAY1)" показываем просто
    // "DISPLAY1", это и короче, и не занимает лишнее место в узких местах (поповер
    // трея, бегущая строка). У мониторов с реальным именем оно по-прежнему видно.
    private const string GenericFriendlyName = "Generic PnP Monitor";

    public static string Format(MonitorInfo monitor)
    {
        var adapterShortName = monitor.AdapterDeviceName.TrimStart('\\', '.');
        return string.Equals(monitor.FriendlyName, GenericFriendlyName, StringComparison.OrdinalIgnoreCase)
            ? adapterShortName
            : $"{monitor.FriendlyName} ({adapterShortName})";
    }
}
