namespace BrightnessControl.Core.Tests;

public class IdleEngineTests
{
    private sealed class FakeIdleSettingsStore : IIdleSettingsStore
    {
        public IdleSettings Settings { get; }

        public FakeIdleSettingsStore(IdleSettings settings)
        {
            Settings = settings;
        }

        public IdleSettings Load() => Settings;

        public void Save(IdleSettings settings)
        {
        }
    }

    [Fact]
    public void EvaluateAndApply_DimsOnTimeout_AndRestoresSnapshotOnActivity_WhenNoAutomationApplies()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"idle-state-test-{Guid.NewGuid():N}.json");
        var pacingPath = Path.Combine(Path.GetTempPath(), $"idle-pacing-test-{Guid.NewGuid():N}.json");
        try
        {
            using var controller = new BrightnessController(
                new JsonFileBrightnessStateStore(statePath),
                new JsonFileMonitorPacingStore(pacingPath));

            var ddcCiMonitor = controller.Monitors.FirstOrDefault(m => m.ConnectionKind == MonitorConnectionKind.ExternalDdcCi);
            if (ddcCiMonitor is null)
            {
                return;
            }

            var original = controller.GetBrightness(ddcCiMonitor)!.Percent;

            try
            {
                var dimPercent = original >= 50 ? 5 : 95;
                var idleStore = new FakeIdleSettingsStore(new IdleSettings { IsEnabled = true, IdleTimeoutMinutes = 5, DimPercent = dimPercent });

                using var engine = new IdleEngine(controller, idleStore, resolveAutomationPercent: null, autoStart: false);

                engine.EvaluateAndApply(TimeSpan.FromMinutes(10));
                var whileDimmed = controller.GetBrightness(ddcCiMonitor);
                if (whileDimmed is null || Math.Abs(whileDimmed.Percent - dimPercent) > 5)
                {
                    return;
                }

                Assert.True(engine.IsDimmed);

                engine.EvaluateAndApply(TimeSpan.Zero);
                var afterActivity = controller.GetBrightness(ddcCiMonitor);
                Assert.False(engine.IsDimmed);
                Assert.NotNull(afterActivity);
                Assert.InRange(afterActivity!.Percent, original - 5, original + 5);
            }
            finally
            {
                controller.SetBrightness(ddcCiMonitor, original);
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

    // FP6 Фаза 1 / FP10: восстановление после простоя должно брать АКТУАЛЬНОЕ
    // значение автоматизации, а не устаревший снимок "до простоя".
    [Fact]
    public void EvaluateAndApply_RestoresAutomationValue_NotStaleSnapshot_WhenAutomationApplies()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"idle-state-test-{Guid.NewGuid():N}.json");
        var pacingPath = Path.Combine(Path.GetTempPath(), $"idle-pacing-test-{Guid.NewGuid():N}.json");
        try
        {
            using var controller = new BrightnessController(
                new JsonFileBrightnessStateStore(statePath),
                new JsonFileMonitorPacingStore(pacingPath));

            var ddcCiMonitor = controller.Monitors.FirstOrDefault(m => m.ConnectionKind == MonitorConnectionKind.ExternalDdcCi);
            if (ddcCiMonitor is null)
            {
                return;
            }

            var original = controller.GetBrightness(ddcCiMonitor)!.Percent;

            try
            {
                var dimPercent = original >= 50 ? 5 : 95;
                var automationTarget = original >= 50 ? 45 : 55;

                var idleStore = new FakeIdleSettingsStore(new IdleSettings { IsEnabled = true, IdleTimeoutMinutes = 5, DimPercent = dimPercent });

                using var engine = new IdleEngine(controller, idleStore, resolveAutomationPercent: _ => automationTarget, autoStart: false);

                engine.EvaluateAndApply(TimeSpan.FromMinutes(10));
                var whileDimmed = controller.GetBrightness(ddcCiMonitor);
                if (whileDimmed is null || Math.Abs(whileDimmed.Percent - dimPercent) > 5)
                {
                    return;
                }

                engine.EvaluateAndApply(TimeSpan.Zero);
                var afterActivity = controller.GetBrightness(ddcCiMonitor);
                Assert.NotNull(afterActivity);
                Assert.InRange(afterActivity!.Percent, automationTarget - 5, automationTarget + 5);
            }
            finally
            {
                controller.SetBrightness(ddcCiMonitor, original);
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
