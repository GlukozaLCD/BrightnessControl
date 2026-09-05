using System.Management;

namespace BrightnessControl.Core;

public sealed class WmiBrightnessProvider
{
    public BrightnessLevel? GetBrightness(MonitorInfo monitor)
    {
        var hardwareId = DeviceIdentity.ExtractHardwareId(monitor.DeviceId);
        if (hardwareId is null)
        {
            return null;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, CurrentBrightness FROM WmiMonitorBrightness");
            foreach (var baseItem in searcher.Get())
            {
                using (baseItem)
                {
                    if (baseItem["InstanceName"] is string instanceName &&
                        instanceName.Contains(hardwareId, StringComparison.OrdinalIgnoreCase) &&
                        baseItem["CurrentBrightness"] is byte current)
                    {
                        return new BrightnessLevel(0, current, 100);
                    }
                }
            }
        }
        catch (ManagementException)
        {
            return null;
        }

        return null;
    }

    public bool SetBrightness(MonitorInfo monitor, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);

        var hardwareId = DeviceIdentity.ExtractHardwareId(monitor.DeviceId);
        if (hardwareId is null)
        {
            return false;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName FROM WmiMonitorBrightnessMethods");
            foreach (var baseItem in searcher.Get())
            {
                using var item = (ManagementObject)baseItem;
                if (item["InstanceName"] is string instanceName &&
                    instanceName.Contains(hardwareId, StringComparison.OrdinalIgnoreCase))
                {
                    // WmiSetBrightness(uint32 Timeout, uint8 Brightness) — Timeout=0 значит
                    // применить немедленно, без плавного перехода.
                    item.InvokeMethod("WmiSetBrightness", new object[] { 0u, (byte)percent });
                    return true;
                }
            }
        }
        catch (ManagementException)
        {
            return false;
        }

        return false;
    }
}
