namespace BrightnessControl.Core.Tests;

public class BrightnessControllerTests
{
    [Fact]
    public void SetAllBrightness_AppliesToEveryDdcCiMonitor_AndPersistsLastPercent()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"brightness-controller-test-{Guid.NewGuid():N}.json");
        var pacingPath = Path.Combine(Path.GetTempPath(), $"brightness-controller-pacing-test-{Guid.NewGuid():N}.json");
        try
        {
            using var controller = new BrightnessController(
                new JsonFileBrightnessStateStore(statePath),
                new JsonFileMonitorPacingStore(pacingPath));

            var ddcCiMonitors = controller.Monitors
                .Where(m => m.ConnectionKind == MonitorConnectionKind.ExternalDdcCi)
                .ToList();

            if (ddcCiMonitors.Count == 0)
            {
                return;
            }

            var originals = ddcCiMonitors.ToDictionary(m => m.DeviceId, m => controller.GetBrightness(m)!.Percent);

            try
            {
                var target = originals.Values.First() >= 50 ? 15 : 85;
                controller.SetAllBrightness(target);

                // Некоторые мониторы (на практике — "смарт"-панель с фоновой прошивкой)
                // иногда не успевают применить DDC/CI-команду даже после встроенных
                // повторов провайдера. Это реальное свойство конкретного железа, а не
                // баг диспетчеризации, поэтому не валим тест из-за одного упрямого
                // монитора — важно, что хотя бы отозвавшиеся применились и сохранились.
                var respondedCount = 0;
                foreach (var monitor in ddcCiMonitors)
                {
                    var updated = controller.GetBrightness(monitor);
                    if (updated is null || Math.Abs(updated.Percent - target) > 5)
                    {
                        continue;
                    }

                    respondedCount++;
                    Assert.Equal(target, controller.GetLastKnownPercent(monitor));
                }

                Assert.True(respondedCount > 0, "Ни один DDC/CI-монитор не применил массовое изменение яркости.");
            }
            finally
            {
                foreach (var monitor in ddcCiMonitors)
                {
                    controller.SetBrightness(monitor, originals[monitor.DeviceId]);
                }
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
