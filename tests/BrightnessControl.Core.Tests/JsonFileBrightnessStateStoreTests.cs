namespace BrightnessControl.Core.Tests;

public class JsonFileBrightnessStateStoreTests
{
    [Fact]
    public void SetAndGet_PersistsAcrossNewInstance()
    {
        var path = Path.Combine(Path.GetTempPath(), $"brightness-state-test-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonFileBrightnessStateStore(path);
            Assert.Null(store.GetLastPercent("MONITOR_A"));

            store.SetLastPercent("MONITOR_A", 42);

            var reloaded = new JsonFileBrightnessStateStore(path);
            Assert.Equal(42, reloaded.GetLastPercent("MONITOR_A"));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
