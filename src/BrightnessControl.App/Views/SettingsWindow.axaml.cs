using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using BrightnessControl.App.Services;
using BrightnessControl.Core;

namespace BrightnessControl.App.Views;

public partial class SettingsWindow : Window
{
    private readonly BrightnessController _controller;
    private readonly AppSettingsStore _appSettingsStore;
    private readonly Dictionary<string, CoalescingBrightnessApplier> _perMonitorAppliers = new();
    private readonly List<Slider> _monitorSliders = new();

    // Нужен только для XAML-дизайнера/превью — реальный экземпляр всегда создаётся
    // через конструктор ниже, с реальными зависимостями.
    public SettingsWindow()
    {
        _controller = null!;
        _appSettingsStore = null!;
        InitializeComponent();
    }

    public SettingsWindow(BrightnessController controller, AppSettingsStore appSettingsStore)
    {
        _controller = controller;
        _appSettingsStore = appSettingsStore;
        InitializeComponent();
        BuildContent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Открывается по центру КОНКРЕТНОГО монитора, на котором произошло взаимодействие
    // с треем, а не только на основном мониторе системы — это явное требование проекта.
    public void ShowCenteredOn(MonitorBounds bounds)
    {
        if (!IsVisible)
        {
            Show();
        }

        Position = new PixelPoint(
            bounds.X + (bounds.Width - (int)Width) / 2,
            bounds.Y + (bounds.Height - (int)Height) / 2);
    }

    private void BuildContent()
    {
        var root = this.FindControl<StackPanel>("RootPanel")!;

        root.Children.Add(new TextBlock { Text = "Оформление", FontWeight = Avalonia.Media.FontWeight.Bold });
        root.Children.Add(BuildThemeSelector());
        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });

        root.Children.Add(new TextBlock { Text = "Все мониторы", FontWeight = Avalonia.Media.FontWeight.Bold });
        // "Все сразу" — чистый синхронизатор: сам не пишет в железо, а просто
        // двигает слайдер каждого монитора, который уже сам отвечает за запись.
        AddSliderRow(root, "Все сразу", ComputeAveragePercent(), percent =>
        {
            foreach (var slider in _monitorSliders)
            {
                slider.Value = percent;
            }
        });

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        root.Children.Add(new TextBlock { Text = "По отдельности", FontWeight = Avalonia.Media.FontWeight.Bold });

        foreach (var monitor in _controller.Monitors)
        {
            var current = _controller.GetBrightness(monitor)?.Percent ?? 50;
            var slider = AddSliderRow(root, MonitorLabel.Format(monitor), current, percent =>
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
            _monitorSliders.Add(slider);
        }
    }

    private ComboBox BuildThemeSelector()
    {
        var options = new (AppThemePreference Value, string Label)[]
        {
            (AppThemePreference.System, "Системная"),
            (AppThemePreference.Light, "Светлая"),
            (AppThemePreference.Dark, "Тёмная"),
        };

        var current = _appSettingsStore.Load().Theme;
        var comboBox = new ComboBox
        {
            ItemsSource = options.Select(o => o.Label).ToList(),
            SelectedIndex = Array.FindIndex(options, o => o.Value == current),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 160,
        };

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedIndex < 0)
            {
                return;
            }

            var selected = options[comboBox.SelectedIndex].Value;
            App.ApplyTheme(selected);
            var settings = _appSettingsStore.Load();
            settings.Theme = selected;
            _appSettingsStore.Save(settings);
        };

        return comboBox;
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

    // Слайдер "прилипает" к шагу 5 (легко попасть в 30, 55 и т.д.), а кнопки ±
    // дают точную подстройку на 1% — например, до 29 удобнее дойти кнопкой от
    // 30, чем медленно тащить слайдер между тиками.
    //
    // Во время реального перетаскивания слайдер пересекает много тиков подряд —
    // если писать в железо на КАЖДЫЙ тик, капризные DDC/CI-мониторы (см. FP1/FP2)
    // захлёбываются частыми командами и перестают отвечать. Поэтому во время
    // перетаскивания меняется только подпись (live), а в железо значение
    // применяется один раз — по отпусканию кнопки мыши, по клику ±, или когда
    // слайдер двигает не пользователь напрямую (например, синхронизация от
    // общего слайдера "Все сразу").
    private static Slider AddSliderRow(StackPanel root, string label, int initialPercent, Action<int> onChanged)
    {
        var header = new TextBlock { Text = $"{label}: {initialPercent}%" };
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = initialPercent,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
        };

        var isDragging = false;

        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty)
            {
                return;
            }

            var percent = (int)slider.Value;
            header.Text = $"{label}: {percent}%";
            if (!isDragging)
            {
                onChanged(percent);
            }
        };

        slider.AddHandler(InputElement.PointerPressedEvent, (_, _) => isDragging = true, handledEventsToo: true);
        slider.AddHandler(InputElement.PointerReleasedEvent, (_, _) =>
        {
            isDragging = false;
            onChanged((int)slider.Value);
        }, handledEventsToo: true);

        var minusButton = new Button { Content = "−", Width = 32 };
        var plusButton = new Button { Content = "+", Width = 32 };
        minusButton.Click += (_, _) => slider.Value = Math.Clamp(slider.Value - 1, slider.Minimum, slider.Maximum);
        plusButton.Click += (_, _) => slider.Value = Math.Clamp(slider.Value + 1, slider.Minimum, slider.Maximum);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };
        Grid.SetColumn(minusButton, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(plusButton, 2);
        row.Children.Add(minusButton);
        row.Children.Add(slider);
        row.Children.Add(plusButton);

        root.Children.Add(header);
        root.Children.Add(row);
        return slider;
    }
}
