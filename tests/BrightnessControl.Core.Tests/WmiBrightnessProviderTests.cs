namespace BrightnessControl.Core.Tests;

public class WmiBrightnessProviderTests
{
    [Fact]
    public void GetAndSetBrightness_RoundTrips_OnInternalPanel()
    {
        var monitor = MonitorEnumerator.EnumerateMonitors()
            .FirstOrDefault(m => m.ConnectionKind == MonitorConnectionKind.InternalPanel);

        if (monitor is null)
        {
            return;
        }

        var provider = new WmiBrightnessProvider();
        var original = provider.GetBrightness(monitor);
        Assert.NotNull(original);

        try
        {
            var targetPercent = original!.Percent >= 50 ? 20 : 80;
            var setOk = provider.SetBrightness(monitor, targetPercent);
            Assert.True(setOk);

            var updated = provider.GetBrightness(monitor);
            Assert.NotNull(updated);
            Assert.InRange(updated!.Percent, targetPercent - 5, targetPercent + 5);
        }
        finally
        {
            provider.SetBrightness(monitor, original!.Percent);
        }
    }
}
