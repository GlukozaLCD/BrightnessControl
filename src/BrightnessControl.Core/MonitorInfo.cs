namespace BrightnessControl.Core;

public sealed record MonitorInfo(
    string DeviceId,
    string FriendlyName,
    string AdapterDeviceName,
    MonitorConnectionKind ConnectionKind,
    MonitorBounds Bounds);
