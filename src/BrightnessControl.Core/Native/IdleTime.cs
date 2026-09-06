using System.Runtime.InteropServices;

namespace BrightnessControl.Core.Native;

internal static class IdleTime
{
    // Нет системного события "пользователь бездействует N минут" — только точка
    // времени последнего ввода, поэтому простой определяется опросом (см. IdleEngine),
    // а не хуком/message loop, в отличие от ForegroundAppWatcher (FP5).
    public static TimeSpan GetIdleDuration()
    {
        var info = new User32.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<User32.LASTINPUTINFO>() };
        if (!User32.GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        var idleTicks = (uint)Environment.TickCount - info.dwTime;
        return TimeSpan.FromMilliseconds(idleTicks);
    }
}
