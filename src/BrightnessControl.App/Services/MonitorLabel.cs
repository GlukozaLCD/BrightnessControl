using BrightnessControl.Core;

namespace BrightnessControl.App.Services;

// Windows нередко отдаёт одинаковое общее имя ("Generic PnP Monitor") для
// нескольких разных мониторов — без номера адаптера их не отличить ни в
// трей-меню, ни в окне настроек.
public static class MonitorLabel
{
    public static string Format(MonitorInfo monitor)
    {
        var adapterShortName = monitor.AdapterDeviceName.TrimStart('\\', '.');
        return $"{monitor.FriendlyName} ({adapterShortName})";
    }
}
