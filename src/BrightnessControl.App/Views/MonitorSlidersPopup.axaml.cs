using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
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
    private readonly MonitorLockService _lockService;
    private readonly Dictionary<string, CoalescingBrightnessApplier> _perMonitorAppliers = new();
    private readonly List<(MonitorInfo Monitor, Slider Slider)> _monitorSliders = new();

    // Нужен только для XAML-дизайнера/превью.
    public MonitorSlidersPopup()
    {
        _controller = null!;
        _lockService = null!;
        InitializeComponent();
    }

    public MonitorSlidersPopup(BrightnessController controller, int sliderStepPercent, MonitorLockService lockService)
    {
        _controller = controller;
        _sliderStepPercent = sliderStepPercent;
        _lockService = lockService;
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
        var monitorNameStore = new MonitorNameStore();
        var monitorNames = monitorNameStore.Load();

        foreach (var monitor in _controller.Monitors)
        {
            var current = _controller.GetBrightness(monitor)?.Percent ?? 50;
            var nameControl = BuildEditableMonitorLabel(monitor, monitorNames, monitorNameStore, nameColumnWidth);

            // FP14 — "замочек" на яркость: маленький кружок перед названием
            // монитора (тот же визуальный язык, что уже используют
            // статус-индикаторы в приложении — залитый кружок = состояние
            // включено). Собран здесь же, а не как правка общего
            // SettingsWindow.AddSliderRow — лок нужен ТОЛЬКО в этом окне, не
            // в GlobalSliderPopup/остальных вызовах.
            var lockToggle = BuildLockToggle(monitor);
            var nameWithLock = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            nameWithLock.Children.Add(lockToggle);
            nameWithLock.Children.Add(nameControl);

            var slider = SettingsWindow.AddSliderRow(root, nameWithLock, current, _sliderStepPercent, percent =>
            {
                if (!_perMonitorAppliers.TryGetValue(monitor.DeviceId, out var applier))
                {
                    applier = new CoalescingBrightnessApplier(
                        p => _controller.SetBrightness(monitor, p),
                        () => _controller.GetPacingMs(monitor));
                    _perMonitorAppliers[monitor.DeviceId] = applier;
                }

                applier.Request(percent);
            });
            _monitorSliders.Add((monitor, slider));
        }
    }

    // FP14 — настоящий силуэт замочка (не абстрактный кружок), собран из двух
    // примитивов, тот же приём наложения, что уже использован в
    // TrayIconRenderer.DrawHalfDisc: дужка — полное кольцо (Ellipse, только
    // обводка), корпус — Border, добавленный ПОСЛЕ (значит рисуется поверх) и
    // перекрывающий нижнюю половину дужки, так что видна только верхняя дуга —
    // именно такой силуэт и читается как замок. Залит акцентным цветом =
    // зафиксировано, только контур приглушённым цветом = не зафиксировано.
    private Control BuildLockToggle(MonitorInfo monitor)
    {
        const double canvasSize = 15;
        const double shackleSize = 8;
        const double bodyWidth = 12;
        const double bodyHeight = 8;

        var canvas = new Canvas
        {
            Width = canvasSize,
            Height = canvasSize,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        var shackle = new Ellipse
        {
            Width = shackleSize,
            Height = shackleSize,
            Fill = Brushes.Transparent,
            StrokeThickness = 1.6,
        };
        Canvas.SetLeft(shackle, (canvasSize - shackleSize) / 2);
        Canvas.SetTop(shackle, 1);

        var body = new Border
        {
            Width = bodyWidth,
            Height = bodyHeight,
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(1.6),
        };
        Canvas.SetLeft(body, (canvasSize - bodyWidth) / 2);
        Canvas.SetTop(body, canvasSize - bodyHeight - 1);

        canvas.Children.Add(shackle);
        canvas.Children.Add(body);

        void Refresh()
        {
            var locked = _lockService.IsLocked(monitor);
            if (locked)
            {
                shackle.Bind(Shape.StrokeProperty, this.GetResourceObservable("AppAccentBrush"));
                body.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppAccentBrush"));
                body.BorderThickness = new Thickness(0);
            }
            else
            {
                shackle.Bind(Shape.StrokeProperty, this.GetResourceObservable("AppLineStrong"));
                body.Background = Brushes.Transparent;
                body.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppLineStrong"));
                body.BorderThickness = new Thickness(1.6);
            }

            ToolTip.SetTip(canvas, locked
                ? "Яркость зафиксирована — автоматика (расписание/профили/простой) её не трогает. Нажмите, чтобы снять."
                : "Нажмите, чтобы зафиксировать текущую яркость — автоматика перестанет её менять (ручное управление слайдером остаётся доступным).");
        }

        Refresh();
        canvas.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _lockService.SetLocked(monitor, !_lockService.IsLocked(monitor));
            Refresh();
        };

        return canvas;
    }

    // Клик по названию монитора (или по значку пера) переключает его на инлайн
    // TextBox — отдельное окно ("как для выбора цвета иконки трея") тут не годится:
    // это окно и GlobalSliderPopup закрываются друг без друга при потере активности
    // (см. GlobalSliderPopup.EvaluateShouldClose), а открытие СТОРОННЕГО модального
    // окна как раз и вызывало бы такую потерю активности. Инлайн-редактирование —
    // это просто ещё один контрол ВНУТРИ уже открытого окна, переключение фокуса
    // между контролами одного окна не трогает его активность вовсе.
    //
    // Значок пера — маленький кружок в ЛЕВОМ ВЕРХНЕМ углу (не справа, как раньше
    // делали для похожей кнопки в галерее иконок трея) — по явному указанию
    // пользователя. Марки-строке освобождается место слева (см. pencilReserve),
    // чтобы перо не перекрывало первую букву названия.
    private static Control BuildEditableMonitorLabel(
        MonitorInfo monitor, Dictionary<string, string> monitorNames, MonitorNameStore monitorNameStore, double width)
    {
        var monitorKey = BrightnessController.GetMonitorKey(monitor);
        var defaultName = MonitorLabel.Format(monitor);
        var container = new Panel();
        var isEditing = false;
        var isCommitting = false;
        const double pencilReserve = 12;

        void Rebuild()
        {
            container.Children.Clear();

            if (isEditing)
            {
                var currentCustom = monitorNames.TryGetValue(monitorKey, out var existing) ? existing : string.Empty;
                var textBox = new TextBox { Text = currentCustom, Width = width, FontSize = 12, PlaceholderText = defaultName };

                // Иначе клик, которым пользователь заходит В поле, всплыл бы дальше и
                // ничего плохого не сделал бы здесь — но это на будущее, если сверху
                // когда-нибудь появится ещё один обработчик клика на всей строке.
                textBox.PointerPressed += (_, e) => e.Handled = true;

                void Commit()
                {
                    // Children.Clear() в Rebuild() ниже синхронно отбирает фокус у ещё
                    // "живого" textBox, из-за чего LostFocus срабатывает ПОВТОРНО прямо
                    // посреди этого же вызова — та же гонка, что уже чинили в галерее
                    // иконок трея (переименование форм).
                    if (isCommitting)
                    {
                        return;
                    }

                    isCommitting = true;
                    try
                    {
                        var value = textBox.Text?.Trim();
                        if (string.IsNullOrEmpty(value))
                        {
                            monitorNames.Remove(monitorKey);
                        }
                        else
                        {
                            monitorNames[monitorKey] = value;
                        }

                        monitorNameStore.Save(monitorNames);
                        isEditing = false;
                        Rebuild();
                    }
                    finally
                    {
                        isCommitting = false;
                    }
                }

                textBox.LostFocus += (_, _) => Commit();
                textBox.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        Commit();
                    }
                };

                container.Children.Add(textBox);
                Dispatcher.UIThread.Post(() => textBox.Focus(), DispatcherPriority.Background);
                return;
            }

            var displayName = monitorNames.TryGetValue(monitorKey, out var custom) && !string.IsNullOrEmpty(custom)
                ? custom
                : defaultName;

            var marquee = SettingsWindow.BuildMarqueeLabel(displayName, width - pencilReserve);
            marquee.Margin = new Thickness(pencilReserve, 0, 0, 0);
            marquee.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(marquee, "Нажмите, чтобы переименовать");

            void StartEditing(object? sender, PointerPressedEventArgs e)
            {
                isEditing = true;
                Rebuild();
            }

            marquee.PointerPressed += StartEditing;

            var pencil = new Border
            {
                Width = 13,
                Height = 13,
                CornerRadius = new CornerRadius(6.5),
                Background = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = "✎",
                    FontSize = 8,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            ToolTip.SetTip(pencil, "Переименовать");
            pencil.PointerPressed += StartEditing;

            container.Children.Add(marquee);
            container.Children.Add(pencil);
        }

        Rebuild();
        return container;
    }

    // FP14 — залоченный монитор пропускается ЦЕЛИКОМ: глобальный слайдер ("Все
    // мониторы") не должен утаскивать его значение за собой, лок защищает и от
    // этого действия, не только от автоматики (уточнено пользователем явно —
    // "замок должен блокировать и мою попытку изменить яркость глобальным
    // слайдером тоже"). Прямое управление ЕГО СОБСТВЕННЫМ слайдером (строка
    // этого же монитора здесь, в MonitorSlidersPopup) лок не трогает — это
    // осталось единственным путём менять яркость залоченного монитора вручную.
    public void SetAllSliders(int percent)
    {
        foreach (var (monitor, slider) in _monitorSliders)
        {
            if (_lockService.IsLocked(monitor))
            {
                continue;
            }

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
