namespace BrightnessControl.Core;

public readonly record struct ForegroundAppInfo(string ProcessName, string WindowTitle, string? MonitorAdapterDeviceName);
