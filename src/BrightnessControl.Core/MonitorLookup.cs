namespace BrightnessControl.Core;

public static class MonitorLookup
{
    public static MonitorInfo? FindAtPoint(IReadOnlyList<MonitorInfo> monitors, int x, int y)
    {
        foreach (var monitor in monitors)
        {
            var bounds = monitor.Bounds;
            if (x >= bounds.X && x < bounds.X + bounds.Width && y >= bounds.Y && y < bounds.Y + bounds.Height)
            {
                return monitor;
            }
        }

        return null;
    }
}
