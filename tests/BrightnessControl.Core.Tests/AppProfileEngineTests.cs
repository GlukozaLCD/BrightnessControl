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

    // Расписание выключено — тесты, которые не касаются взаимодействия с FP4,
    // используют эту заглушку явно, чтобы не читать РЕАЛЬНЫЙ schedule.json
    // текущей машины (он может быть включён и содержать боевые правила).
    private static FakeScheduleStore DisabledSchedule() => new(new ScheduleSettings { IsEnabled = false });

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

                using var engine = new AppProfileEngine(controller, new FakeAppProfileStore(settings), new FakeWatcher(), DisabledSchedule());

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

    // FP5 Фаза 3: профиль приоритетнее расписания. При выходе из профиля движок не
    // должен слепо откатывать к снимку "до профиля" — если расписание включено и
    // применимо к этому монитору, восстанавливаемое значение должно быть АКТУАЛЬНЫМ
    // по расписанию на текущий момент, а не устаревшим снимком.
    [Fact]
    public void OnForegroundChanged_RestoresScheduleValue_NotStaleSnapshot_WhenScheduleApplies()
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
                var profileTarget = original >= 50 ? 20 : 85;
                // Единственное правило на 00:00 действует круглые сутки (см.
                // ScheduleEngine.FindActiveRule) — не зависит от реального времени теста.
                var scheduleTarget = original >= 50 ? 45 : 55;

                var profileSettings = new AppProfileSettings
                {
                    IsEnabled = true,
                    Profiles = { new AppProfile { MatchType = AppMatchType.ProcessName, MatchValue = "notepad", Percent = profileTarget } },
                };
                var scheduleSettings = new ScheduleSettings
                {
                    IsEnabled = true,
                    Rules = { new ScheduleRule { Time = TimeOnly.Parse("00:00"), Percent = scheduleTarget } },
                };

                using var engine = new AppProfileEngine(
                    controller,
                    new FakeAppProfileStore(profileSettings),
                    new FakeWatcher(),
                    new FakeScheduleStore(scheduleSettings));

                engine.OnForegroundChanged(new ForegroundAppInfo("notepad", "Untitled - Notepad", monitor.AdapterDeviceName));

                var whileActive = controller.GetBrightness(monitor);
                if (whileActive is null || Math.Abs(whileActive.Percent - profileTarget) > 5)
                {
                    return;
                }

                engine.OnForegroundChanged(new ForegroundAppInfo("explorer", "Проводник", monitor.AdapterDeviceName));

                var afterExit = controller.GetBrightness(monitor);
                Assert.NotNull(afterExit);
                Assert.InRange(afterExit!.Percent, scheduleTarget - 5, scheduleTarget + 5);
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
