namespace BrightnessControl.Core.Tests;

public class MonitorEnumeratorTests
{
    [Fact]
    public void EnumerateMonitors_ReturnsAtLeastOneMonitor()
    {
        var monitors = MonitorEnumerator.EnumerateMonitors();

        Assert.NotEmpty(monitors);
        Assert.All(monitors, m => Assert.False(string.IsNullOrWhiteSpace(m.DeviceId)));
    }
}
