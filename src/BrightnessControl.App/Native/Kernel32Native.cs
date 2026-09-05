using System.Runtime.InteropServices;

namespace BrightnessControl.App.Native;

internal static class Kernel32Native
{
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();
}
