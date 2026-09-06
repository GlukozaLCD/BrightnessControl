using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BrightnessControl.App.Services;
using BrightnessControl.Core;

namespace BrightnessControl.App.Views;

// Открывается по левому клику на иконку трея (правый клик по-прежнему открывает
// SettingsWindow) — лёгкий поповер только с яркостью, без остальных настроек.
// Прицеплен к самой иконке трея (как системные поповеры громкости/сети в Windows),
// а не по центру монитора клика, как SettingsWindow — см. FP9 Фазу 2.
//
// Пересоздаётся заново при каждом открытии (не кэшируется как SettingsWindow) —
// это заодно даёт "бесплатно" всегда свежие текущие значения яркости и всегда
// свёрнутое состояние (панель по монитору — отдельное окно, см. MonitorSlidersPopup,
// и она просто не создаётся, пока её не попросили).
//
// СВОЙ размер/позиция у этого окна больше не меняются НИКОГДА после первого показа —
// разворачивание по монитору происходит в ОТДЕЛЬНОМ окне (MonitorSlidersPopup), а не
// через SizeToContent этого же окна. Раньше это была одна и та же growing/shrinking
// панель, и между сжатием контента и переносом окна проскакивал видимый кадр (рывок).
public partial class GlobalSliderPopup : Window
{
    private readonly BrightnessController _controller;
    private readonly CoalescingBrightnessApplier _globalApplier;
    private readonly int _sliderStepPercent;
    private MonitorSlidersPopup? _monitorSlidersPopup;
    private Button? _expandButton;

    // Нужен только для XAML-дизайнера/превью.
    public GlobalSliderPopup()
    {
        _controller = null!;
        _globalApplier = null!;
        InitializeComponent();
    }

    public GlobalSliderPopup(BrightnessController controller, CoalescingBrightnessApplier globalApplier, int sliderStepPercent)
    {
        _controller = controller;
        _globalApplier = globalApplier;
        _sliderStepPercent = sliderStepPercent;
        InitializeComponent();
        BuildContent();

        // Ведёт себя как поповер: закрывается, стоит только кликнуть мимо ОБОИХ окон
        // сразу (не только этого) — см. EvaluateShouldClose.
        Deactivated += (_, _) => Dispatcher.UIThread.Post(EvaluateShouldClose);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void BuildContent()
    {
        var root = this.FindControl<StackPanel>("Root")!;

        var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = new TextBlock { Text = "Яркость", FontWeight = Avalonia.Media.FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        var expandButton = new Button
        {
            Content = "▼",
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            FontSize = 12,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(expandButton, "Показать яркость по отдельным мониторам");
        _expandButton = expandButton;
        Grid.SetColumn(title, 0);
        Grid.SetColumn(expandButton, 1);
        headerRow.Children.Add(title);
        headerRow.Children.Add(expandButton);
        root.Children.Add(headerRow);

        // ВАЖНО: nameColumnWidth передаётся явно (160) — этот вызов ЗАБЫЛ его передать
        // раньше, из-за чего использовалась ширина по умолчанию (360, рассчитана на
        // широкое SettingsWindow), а это окно всего 260px. Marquee-блок шириной 360px
        // не помещался в окно и переполнял раскладку — вот настоящая причина
        // "сломанной строки", которую не решали ни FormattedText, ни отказ от
        // RenderTransform/ClipToBounds: дело было не в механике бегущей строки, а в
        // одном пропущенном аргументе.
        const double nameColumnWidth = 160;
        SettingsWindow.AddSliderRow(root, "Все мониторы", ComputeAveragePercent(), _sliderStepPercent, percent =>
        {
            _monitorSlidersPopup?.SetAllSliders(percent);
            _globalApplier.Request(percent);
        }, nameColumnWidth);

        expandButton.Click += (_, _) => ToggleMonitorSliders();
    }

    private void ToggleMonitorSliders()
    {
        if (_monitorSlidersPopup is not null)
        {
            _monitorSlidersPopup.Close();
            return;
        }

        var popup = new MonitorSlidersPopup(_controller, _sliderStepPercent);
        _monitorSlidersPopup = popup;
        popup.Closed += (_, _) =>
        {
            _monitorSlidersPopup = null;
            if (_expandButton is not null)
            {
                _expandButton.Content = "▼";
            }
        };
        popup.Deactivated += (_, _) => Dispatcher.UIThread.Post(EvaluateShouldClose);

        var roughHeightGuess = Math.Max(1, _controller.Monitors.Count) * 68;
        popup.ShowAbove(Position, (int)Width, roughHeightGuess);
        _expandButton!.Content = "▲";
    }

    // Два независимых top-level окна должны вести себя как одно целое: клик по
    // "своему" соседнему окну не должен закрывать пару, а клик куда угодно ещё —
    // должен закрыть обе сразу. Проверка отложена на тик диспетчера, чтобы Windows
    // успел передать активацию новому окну ДО того, как мы посмотрим на IsActive.
    private void EvaluateShouldClose()
    {
        if (!IsVisible)
        {
            return;
        }

        if (IsActive || (_monitorSlidersPopup?.IsActive ?? false))
        {
            return;
        }

        _monitorSlidersPopup?.Close();
        Close();
    }

    private int ComputeAveragePercent()
    {
        var monitors = _controller.Monitors;
        if (monitors.Count == 0)
        {
            return 50;
        }

        var sum = 0;
        var count = 0;
        foreach (var monitor in monitors)
        {
            var level = _controller.GetBrightness(monitor);
            if (level is not null)
            {
                sum += level.Percent;
                count++;
            }
        }

        return count > 0 ? sum / count : 50;
    }

    public void ShowNearIcon(MonitorBounds iconRect)
    {
        // Ширина/высота читаются ПОСЛЕ Show() — до показа окна они ещё не
        // обязательно отражают реально измеренный SizeToContent-размер.
        // Сторона/панель — общая логика, см. TrayPopupPlacement (её же
        // использует SettingsWindow и BrightnessHudWindow).
        if (!IsVisible)
        {
            Show();
        }

        Position = TrayPopupPlacement.Compute(Screens, iconRect.X, iconRect.Y, iconRect.Width, iconRect.Height, (int)Width, (int)Height);
        Activate();
    }
}
