namespace BrightnessControl.Core.Tests;

public class DdcCiBrightnessProviderTests
{
    [Fact]
    public void GetAndSetBrightness_RoundTrips_OnFirstDdcCiMonitor()
    {
        var monitor = MonitorEnumerator.EnumerateMonitors()
            .FirstOrDefault(m => m.ConnectionKind == MonitorConnectionKind.ExternalDdcCi);

        if (monitor is null)
        {
            return;
        }

        var provider = new DdcCiBrightnessProvider();
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
