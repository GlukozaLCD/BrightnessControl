namespace BrightnessControl.Core;

internal static class DeviceIdentity
{
    // DeviceId форматов "\\?\DISPLAY#<HWID>#<instance>#{<GUID>}" (из EnumDisplayDevices с
    // EDD_GET_DEVICE_INTERFACE_NAME) и WMI InstanceName форматов "DISPLAY\<HWID>\<instance>"
    // используют разные разделители, но у обоих второй токен — один и тот же PnP hardware ID
    // монитора. Это единственный доступный публичный способ их сопоставить.
    public static string? ExtractHardwareId(string deviceId)
    {
        if (deviceId.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            var interfaceParts = deviceId.Split('#', StringSplitOptions.RemoveEmptyEntries);
            return interfaceParts.Length >= 2 ? interfaceParts[1] : null;
        }

        var classicParts = deviceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return classicParts.Length >= 2 ? classicParts[1] : null;
    }
}
