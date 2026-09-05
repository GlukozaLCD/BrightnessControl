namespace BrightnessControl.Core.Tests;

public class OverlayDimmerTests
{
    [Fact]
    public void CreateSetDimAndDispose_DoesNotThrow()
    {
        using var dimmer = new OverlayDimmer(new MonitorBounds(0, 0, 200, 200));

        dimmer.SetDimPercent(0);
        dimmer.SetDimPercent(50);
        dimmer.SetDimPercent(100);
        dimmer.SetDimPercent(0);
    }

    [Fact]
    public void FallbackProvider_TracksPercentAndDisposesCleanly()
    {
        var monitor = new MonitorInfo(
            DeviceId: "TEST\\FAKE\\0",
            FriendlyName: "Fake Unsupported Monitor",
            AdapterDeviceName: "\\\\.\\DISPLAY_TEST",
            ConnectionKind: MonitorConnectionKind.Unsupported,
            Bounds: new MonitorBounds(0, 0, 100, 100));

        using var provider = new OverlayFallbackBrightnessProvider();

        Assert.Equal(100, provider.GetBrightness(monitor).Percent);

        Assert.True(provider.SetBrightness(monitor, 30));
        Assert.Equal(30, provider.GetBrightness(monitor).Percent);
    }
}
