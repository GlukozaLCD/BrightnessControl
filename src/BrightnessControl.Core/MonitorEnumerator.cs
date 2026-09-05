using BrightnessControl.Core.Native;
using BrightnessControl.Core.Wmi;

namespace BrightnessControl.Core;

public static class MonitorEnumerator
{
    public static IReadOnlyList<MonitorInfo> EnumerateMonitors()
    {
        var internalPanelInstanceNames = WmiMonitorLookup.GetBrightnessCapableInstanceNames();
        var result = new List<MonitorInfo>();

        foreach (var adapter in DisplayTopology.EnumerateAttachedAdapters())
        {
            var hMonitor = DisplayTopology.FindMonitorHandle(adapter.DeviceName, out var rect);
            if (hMonitor == IntPtr.Zero)
            {
                continue;
            }

            var bounds = new MonitorBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

            var monitorDevices = DisplayTopology.EnumerateActiveMonitorDevices(adapter.DeviceName).ToList();
            var physicalMonitors = DisplayTopology.GetPhysicalMonitors(hMonitor);

            try
            {
                for (var i = 0; i < monitorDevices.Count; i++)
                {
                    var device = monitorDevices[i];
                    var hasDdcCi = i < physicalMonitors.Length && TryReadBrightness(physicalMonitors[i].hPhysicalMonitor);
                    var isInternalPanel = ContainsMatchingHardwareId(internalPanelInstanceNames, device.DeviceID);

                    var kind = isInternalPanel
                        ? MonitorConnectionKind.InternalPanel
                        : hasDdcCi
                            ? MonitorConnectionKind.ExternalDdcCi
                            : MonitorConnectionKind.Unsupported;

                    result.Add(new MonitorInfo(
                        DeviceId: device.DeviceID,
                        FriendlyName: device.DeviceString,
                        AdapterDeviceName: adapter.DeviceName,
                        ConnectionKind: kind,
                        Bounds: bounds));
                }
            }
            finally
            {
                if (physicalMonitors.Length > 0)
                {
                    Dxva2.DestroyPhysicalMonitors((uint)physicalMonitors.Length, physicalMonitors);
                }
            }
        }

        return result;
    }

    private static bool TryReadBrightness(IntPtr hPhysicalMonitor)
    {
        return Dxva2.GetMonitorBrightness(hPhysicalMonitor, out _, out _, out _);
    }

    private static bool ContainsMatchingHardwareId(HashSet<string> wmiInstanceNames, string deviceId)
    {
        var hardwareId = DeviceIdentity.ExtractHardwareId(deviceId);
        if (hardwareId is null)
        {
            return false;
        }

        foreach (var instanceName in wmiInstanceNames)
        {
            if (instanceName.Contains(hardwareId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
