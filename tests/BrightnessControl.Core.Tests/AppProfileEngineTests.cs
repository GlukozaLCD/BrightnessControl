namespace BrightnessControl.Core.Tests;

public class AppProfileEngineTests
{
    private sealed class FakeWatcher : IForegroundAppWatcher
    {
        public event Action<ForegroundAppInfo>? ForegroundChanged;

        public void ReportCurrentForegroundWindow()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeAppProfileStore : IAppProfileStore
    {
        public AppProfileSettings Settings { get; }

        public FakeAppProfileStore(AppProfileSettings settings)
        {
            Settings = settings;
        }

        public AppProfileSettings Load() => Settings;

        public void Save(AppProfileSettings settings)
        {
        }
    }

    [Fact]
    public void AppProfile_Matches_ProcessName_IgnoresCaseAndExeSuffix()
    {
        var profile = new AppProfile { MatchType = AppMatchType.ProcessName, MatchValue = "Notepad.exe" };

        Assert.True(profile.Matches("notepad", "Untitled - Notepad"));
        Assert.False(profile.Matches("chrome", "Google Chrome"));
    }

    [Fact]
    public void AppProfile_Matches_WindowTitle_SubstringIgnoresCase()
    {
        var profile = new AppProfile { MatchType = AppMatchType.WindowTitle, MatchValue = "youtube" };

        Assert.True(profile.Matches("chrome", "Cool Video - YouTube - Google Chrome"));
        Assert.False(profile.Matches("chrome", "Google - Google Chrome"));
    }

    [Fact]
    public void OnForegroundChanged_AppliesProfileToItsMonitor_AndRestoresOnExit()
    {
        var pacingPath = Path.Combine(Path.GetTempPath(), $"appprofile-pacing-test-{Guid.NewGuid():N}.json");
        var statePath = Path.Combine(Path.GetTempPath(), $"appprofile-state-test-{Guid.NewGuid():N}.json");
        try
        {
            using var controller = new BrightnessController(
                new JsonFileBrightnessStateStore(statePath),
                new JsonFileMonitorPacingStore(pacingPath));

            var monitor = controller.Monitors.FirstOrDefault(m => m.ConnectionKind == MonitorConnectionKind.ExternalDdcCi);
            if (monitor is null)
            {
                return;
            }

            var original = controller.GetBrightness(monitor)!.Percent;

            try
            {
                var target = original >= 50 ? 20 : 85;
                var settings = new AppProfileSettings
                {
                    IsEnabled = true,
                    Profiles = { new AppProfile { MatchType = AppMatchType.ProcessName, MatchValue = "notepad", Percent = target } },
                };

                using var engine = new AppProfileEngine(controller, new FakeAppProfileStore(settings), new FakeWatcher());

                // "Открыли" совпадающее приложение на этом мониторе.
                engine.OnForegroundChanged(new ForegroundAppInfo("notepad", "Untitled - Notepad", monitor.AdapterDeviceName));

                var whileActive = controller.GetBrightness(monitor);
                if (whileActive is null || Math.Abs(whileActive.Percent - target) > 5)
                {
                    return;
                }

                // "Переключились" на несовпадающее приложение — должно вернуть исходное значение.
                engine.OnForegroundChanged(new ForegroundAppInfo("explorer", "Проводник", monitor.AdapterDeviceName));

                var afterExit = controller.GetBrightness(monitor);
                Assert.NotNull(afterExit);
                Assert.InRange(afterExit!.Percent, original - 5, original + 5);
            }
            finally
            {
                controller.SetBrightness(monitor, original);
            }
        }
        finally
        {
            if (File.Exists(statePath))
            {
                File.Delete(statePath);
            }

            if (File.Exists(pacingPath))
            {
                File.Delete(pacingPath);
            }
        }
    }
}
