using System.Management;

namespace BrightnessControl.Core.Wmi;

internal static class WmiMonitorLookup
{
    // WmiMonitorBrightness (root\wmi) отвечает только для дисплеев, подключённых
    // к ACPI-интерфейсу яркости — на практике это встроенная панель ноутбука.
    public static HashSet<string> GetBrightnessCapableInstanceNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName FROM WmiMonitorBrightness");
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    if (item["InstanceName"] is string instanceName)
                    {
                        names.Add(instanceName);
                    }
                }
            }
        }
        catch (ManagementException)
        {
            // root\wmi недоступен или класс отсутствует (например, в виртуальной машине) —
            // считаем, что встроенной панели с поддержкой WMI-яркости нет.
        }

        return names;
    }
}
