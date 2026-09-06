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

    private sealed class FakeScheduleStore : IScheduleStore
    {
        public ScheduleSettings Settings { get; }

        public FakeScheduleStore(ScheduleSettings settings)
        {
            Settings = settings;
        }

        public ScheduleSettings Load() => Settings;

        public void Save(ScheduleSettings settings)
        {
        }
    }

    [Fact]
    public void EvaluateAndApply_DimsOnTimeout_AndRestoresSnapshotOnActivity_WhenNoScheduleOrProfile()
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
                var scheduleStore = new FakeScheduleStore(new ScheduleSettings { IsEnabled = false });

                using var engine = new IdleEngine(controller, idleStore, scheduleStore, autoStart: false);

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

    // FP6 Фаза 1: восстановление после простоя должно брать АКТУАЛЬНОЕ значение
    // расписания, а не устаревший снимок "до простоя" — тот же принцип, что и в
    // приоритете FP4×FP5 (AppProfileEngine.RestorePrevious).
    [Fact]
    public void EvaluateAndApply_RestoresScheduleValue_NotStaleSnapshot_WhenScheduleApplies()
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
                // Единственное правило на 00:00 действует круглые сутки (см.
                // ScheduleEngine.FindActiveRule) — не зависит от реального времени теста.
                var scheduleTarget = original >= 50 ? 45 : 55;

                var idleStore = new FakeIdleSettingsStore(new IdleSettings { IsEnabled = true, IdleTimeoutMinutes = 5, DimPercent = dimPercent });
                var scheduleStore = new FakeScheduleStore(new ScheduleSettings
                {
                    IsEnabled = true,
                    Rules = { new ScheduleRule { Time = TimeOnly.Parse("00:00"), Percent = scheduleTarget } },
                });

                using var engine = new IdleEngine(controller, idleStore, scheduleStore, autoStart: false);

                engine.EvaluateAndApply(TimeSpan.FromMinutes(10));
                var whileDimmed = controller.GetBrightness(ddcCiMonitor);
                if (whileDimmed is null || Math.Abs(whileDimmed.Percent - dimPercent) > 5)
                {
                    return;
                }

                engine.EvaluateAndApply(TimeSpan.Zero);
                var afterActivity = controller.GetBrightness(ddcCiMonitor);
                Assert.NotNull(afterActivity);
                Assert.InRange(afterActivity!.Percent, scheduleTarget - 5, scheduleTarget + 5);
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
