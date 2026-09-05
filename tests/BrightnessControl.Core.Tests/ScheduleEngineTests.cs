namespace BrightnessControl.Core.Tests;

public class ScheduleEngineTests
{
    private static ScheduleRule Rule(string time, int percent) => new()
    {
        Time = TimeOnly.Parse(time),
        Percent = percent,
    };

    [Fact]
    public void FindActiveRule_PicksLatestRuleAtOrBeforeNow()
    {
        var rules = new List<ScheduleRule>
        {
            Rule("08:00", 80),
            Rule("13:00", 60),
            Rule("22:00", 20),
        };

        var active = ScheduleEngine.FindActiveRule(rules, TimeOnly.Parse("14:30"));

        Assert.Equal(60, active.Percent);
    }

    [Fact]
    public void FindActiveRule_ExactMatchIsActive()
    {
        var rules = new List<ScheduleRule>
        {
            Rule("08:00", 80),
            Rule("13:00", 60),
        };

        var active = ScheduleEngine.FindActiveRule(rules, TimeOnly.Parse("13:00"));

        Assert.Equal(60, active.Percent);
    }

    [Fact]
    public void FindActiveRule_BeforeEarliestRule_WrapsToLastRuleOfPreviousDay()
    {
        var rules = new List<ScheduleRule>
        {
            Rule("08:00", 80),
            Rule("13:00", 60),
            Rule("22:00", 20),
        };

        var active = ScheduleEngine.FindActiveRule(rules, TimeOnly.Parse("02:00"));

        Assert.Equal(20, active.Percent);
    }

    [Fact]
    public void EvaluateAndApply_DoesNotReapplySameRuleOnConsecutiveTicks()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"schedule-state-test-{Guid.NewGuid():N}.json");
        var pacingPath = Path.Combine(Path.GetTempPath(), $"schedule-pacing-test-{Guid.NewGuid():N}.json");
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

            var store = new FakeScheduleStore(new ScheduleSettings
            {
                IsEnabled = true,
                Rules = { Rule("00:00", original >= 50 ? 15 : 85) },
            });

            using var engine = new ScheduleEngine(controller, store, autoStart: false);

            try
            {
                var target = store.Settings.Rules[0].Percent;

                engine.EvaluateAndApply(DateTime.Today.AddHours(9));
                var afterFirstTick = controller.GetBrightness(ddcCiMonitor);
                if (afterFirstTick is null || Math.Abs(afterFirstTick.Percent - target) > 5)
                {
                    return;
                }

                // Ручная "корректировка", имитирующая пользователя.
                var manualValue = target >= 50 ? 30 : 70;
                controller.SetBrightness(ddcCiMonitor, manualValue);

                // То же самое правило всё ещё активно (время не пересекло следующую
                // точку расписания) — движок не должен переприменять и затирать
                // ручное значение.
                engine.EvaluateAndApply(DateTime.Today.AddHours(9).AddMinutes(1));
                var afterSecondTick = controller.GetBrightness(ddcCiMonitor);
                Assert.NotNull(afterSecondTick);
                Assert.InRange(afterSecondTick!.Percent, manualValue - 5, manualValue + 5);
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
}
