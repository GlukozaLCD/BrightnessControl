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

    // nameOverrides — из MonitorNameStore: пользовательское имя (переименование в
    // настройках) побеждает всё остальное, если задано и не пустое. Параметр
    // необязательный, чтобы места, которым переименование не важно, не были
    // обязаны прокидывать словарь.
    public static string Format(MonitorInfo monitor, IReadOnlyDictionary<string, string>? nameOverrides = null)
    {
        if (nameOverrides is not null
            && nameOverrides.TryGetValue(BrightnessController.GetMonitorKey(monitor), out var custom)
            && !string.IsNullOrEmpty(custom))
        {
            return custom;
        }

        var adapterShortName = monitor.AdapterDeviceName.TrimStart('\\', '.');
        return string.Equals(monitor.FriendlyName, GenericFriendlyName, StringComparison.OrdinalIgnoreCase)
            ? adapterShortName
            : $"{monitor.FriendlyName} ({adapterShortName})";
    }
}
