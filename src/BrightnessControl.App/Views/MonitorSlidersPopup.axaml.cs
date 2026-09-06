using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BrightnessControl.App.Services;
using BrightnessControl.Core;

namespace BrightnessControl.App.Views;

// Отдельное окно (не панель внутри GlobalSliderPopup) — так у главного поповера с
// общим слайдером вообще нет причин менять свой размер/позицию при разворачивании
// по монитору: он просто есть, а это окно появляется/исчезает целиком рядом с ним.
// Раньше один и тот же поповер расширялся/сжимался сам (SizeToContent), и между
// сжатием контента и переносом окна проскакивал видимый кадр — выглядело как рывок.
public partial class MonitorSlidersPopup : Window
{
    private readonly BrightnessController _controller;
    private readonly int _sliderStepPercent;
    private readonly Dictionary<string, CoalescingBrightnessApplier> _perMonitorAppliers = new();
    private readonly List<Slider> _monitorSliders = new();

    // Нужен только для XAML-дизайнера/превью.
    public MonitorSlidersPopup()
    {
        _controller = null!;
        InitializeComponent();
    }

    public MonitorSlidersPopup(BrightnessController controller, int sliderStepPercent)
    {
        _controller = controller;
        _sliderStepPercent = sliderStepPercent;
        InitializeComponent();
        BuildContent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void BuildContent()
    {
        var root = this.FindControl<StackPanel>("Root")!;
        const double nameColumnWidth = 160;

        foreach (var monitor in _controller.Monitors)
        {
            var current = _controller.GetBrightness(monitor)?.Percent ?? 50;
            var slider = SettingsWindow.AddSliderRow(root, MonitorLabel.Format(monitor), current, _sliderStepPercent, percent =>
            {
                if (!_perMonitorAppliers.TryGetValue(monitor.DeviceId, out var applier))
                {
                    applier = new CoalescingBrightnessApplier(
                        p => _controller.SetBrightness(monitor, p),
                        () => _controller.GetPacingMs(monitor));
                    _perMonitorAppliers[monitor.DeviceId] = applier;
                }

                applier.Request(percent);
            }, nameColumnWidth);
            _monitorSliders.Add(slider);
        }
    }

    public void SetAllSliders(int percent)
    {
        foreach (var slider in _monitorSliders)
        {
            slider.Value = percent;
        }
    }

    // Показывается один раз при открытии (содержимое дальше не меняется — в отличие
    // от прежнего подхода, тут нечему "прыгать" после первого появления), поэтому
    // достаточно одноразового уточнения позиции по факту реального размера.
    public void ShowAbove(PixelPoint mainPopupPosition, int mainPopupWidth, int roughHeightGuess)
    {
        Position = new PixelPoint(
            mainPopupPosition.X + mainPopupWidth - (int)Width,
            mainPopupPosition.Y - roughHeightGuess - 4);

        Show();

        void Handler(object? sender, EventArgs e)
        {
            LayoutUpdated -= Handler;
            Position = new PixelPoint(
                mainPopupPosition.X + mainPopupWidth - (int)Width,
                mainPopupPosition.Y - (int)Height - 4);
        }

        LayoutUpdated += Handler;
    }
}
