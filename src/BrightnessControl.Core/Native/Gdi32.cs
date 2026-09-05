using System.Runtime.InteropServices;

namespace BrightnessControl.Core.Native;

internal static class Gdi32
{
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(int crColor);
}
