using System.Collections.Concurrent;

namespace BrightnessControl.Core;

// Для мониторов без DDC/CI у нас нет способа прочитать реальную аппаратную
// яркость — считаем 100% "исходным" состоянием (без затемнения) и дальше
// умеем только программно затемнять оверлеем поверх такого монитора.
public sealed class OverlayFallbackBrightnessProvider : IDisposable
{
    private sealed record State(OverlayDimmer Dimmer, int Percent);

    private readonly ConcurrentDictionary<string, State> _state = new();

    public BrightnessLevel GetBrightness(MonitorInfo monitor)
    {
        var percent = _state.TryGetValue(monitor.DeviceId, out var state) ? state.Percent : 100;
        return new BrightnessLevel(0, (uint)percent, 100);
    }

    public bool SetBrightness(MonitorInfo monitor, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);

        _state.AddOrUpdate(
            monitor.DeviceId,
            _ =>
            {
                var dimmer = new OverlayDimmer(monitor.Bounds);
                dimmer.SetDimPercent(100 - percent);
                return new State(dimmer, percent);
            },
            (_, existing) =>
            {
                existing.Dimmer.SetDimPercent(100 - percent);
                return existing with { Percent = percent };
            });

        return true;
    }

    public void Dispose()
    {
        foreach (var state in _state.Values)
        {
            state.Dimmer.Dispose();
        }

        _state.Clear();
    }
}
