using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BrightnessControl.App.Services;
using BrightnessControl.Core;

namespace BrightnessControl.App.Views;

public partial class SettingsWindow : Window
{
    private readonly BrightnessController _controller;
    private readonly AppSettings _appSettings;
    private readonly AppSettingsStore _appSettingsStore;
    private readonly TraySettings _traySettings;
    private readonly TraySettingsStore _traySettingsStore;
    private readonly Action _onExitRequested;
    private readonly Dictionary<string, CoalescingBrightnessApplier> _perMonitorAppliers = new();
    private readonly List<Slider> _monitorSliders = new();

    // Нужен только для XAML-дизайнера/превью — реальный экземпляр всегда создаётся
    // через конструктор ниже, с реальными зависимостями.
    public SettingsWindow()
    {
        _controller = null!;
        _appSettings = null!;
        _appSettingsStore = null!;
        _traySettings = null!;
        _traySettingsStore = null!;
        _onExitRequested = () => { };
        InitializeComponent();
    }

    public SettingsWindow(
        BrightnessController controller,
        AppSettings appSettings,
        AppSettingsStore appSettingsStore,
        TraySettings traySettings,
        TraySettingsStore traySettingsStore,
        Action onExitRequested)
    {
        _controller = controller;
        _appSettings = appSettings;
        _appSettingsStore = appSettingsStore;
        _traySettings = traySettings;
        _traySettingsStore = traySettingsStore;
        _onExitRequested = onExitRequested;
        InitializeComponent();
        BuildContent();
        SetupCloseButton();

        // Ведёт себя как всплывающее меню трея: закрывается, стоит только кликнуть
        // мимо — а не как обычное окно настроек, которое остаётся открытым.
        Deactivated += (_, _) => Close();
    }

    // Окно без рамки (WindowDecorations="None"), поэтому своего крестика у него нет —
    // рисуем свой: красный фон и белый крестик всегда, а при наведении крестик
    // становится жирнее и фон сменяется диагональным переливом красный→белый.
    // Обычная Avalonia Button поверх любого заданного фона рисует свой полупрозрачный
    // оверлей наведения из темы (отсюда "чёрное/прозрачное пятно" вместо градиента),
    // поэтому здесь используется Border — у него нет встроенного состояния наведения,
    // и заданный фон отображается ровно так, как задан.
    private void SetupCloseButton()
    {
        var closeButton = this.FindControl<Border>("CloseButton")!;
        var glyph = this.FindControl<TextBlock>("CloseButtonGlyph")!;

        var red = Avalonia.Media.Color.FromRgb(0xE8, 0x11, 0x23);
        var solidRed = new Avalonia.Media.SolidColorBrush(red);

        // Ширина белой полосы "блика" в долях ширины диагонали кнопки.
        const double bandWidth = 0.28;
        var sweepGradient = new Avalonia.Media.LinearGradientBrush
        {
            // Из правого верхнего угла в левый нижний.
            StartPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new Avalonia.Media.GradientStop(red, 0),
                new Avalonia.Media.GradientStop(Avalonia.Media.Colors.White, 0),
                new Avalonia.Media.GradientStop(red, 0),
            },
        };

        closeButton.Background = solidRed;
        glyph.Foreground = Avalonia.Media.Brushes.White;
        glyph.FontWeight = Avalonia.Media.FontWeight.Normal;
        closeButton.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);

        DispatcherTimer? sweepTimer = null;
        var progress = 0.0;

        // Быстрые повторные наведения/уходы мышью не должны дёргать анимацию туда-сюда:
        // если блик уже бежит — даём ему доиграть до конца, не перезапуская и не обрывая
        // резко при уходе курсора. PointerEntered/PointerExited влияют только на жирность
        // крестика, которая мгновенна и не может выглядеть "дёргано".
        closeButton.PointerEntered += (_, _) =>
        {
            glyph.FontWeight = Avalonia.Media.FontWeight.Bold;

            if (sweepTimer is not null)
            {
                return;
            }

            progress = 0.0;
            closeButton.Background = sweepGradient;
            sweepTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            sweepTimer.Tick += (_, _) =>
            {
                progress += 0.06;
                if (progress >= 1.0)
                {
                    sweepTimer?.Stop();
                    sweepTimer = null;
                    closeButton.Background = solidRed;
                    return;
                }

                // Полоса въезжает с одного угла (центр < 0) и выезжает за противоположный
                // (центр > 1) — на краях кнопка целиком красная, в середине пути виден блик.
                var center = -bandWidth + progress * (1 + 2 * bandWidth);
                sweepGradient.GradientStops[0].Offset = Math.Clamp(center - bandWidth, 0, 1);
                sweepGradient.GradientStops[1].Offset = Math.Clamp(center, 0, 1);
                sweepGradient.GradientStops[2].Offset = Math.Clamp(center + bandWidth, 0, 1);
            };
            sweepTimer.Start();
        };
        closeButton.PointerExited += (_, _) =>
        {
            glyph.FontWeight = Avalonia.Media.FontWeight.Normal;
        };
        closeButton.PointerPressed += (_, _) => _onExitRequested();
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
        BuildMonitorsTab(this.FindControl<StackPanel>("MonitorsPanel")!);
        BuildTrayTab(this.FindControl<StackPanel>("TrayPanel")!);
        BuildAppearanceTab(this.FindControl<StackPanel>("AppearancePanel")!);
        BuildScheduleTab(this.FindControl<StackPanel>("SchedulePanel")!);
    }

    private void BuildMonitorsTab(StackPanel root)
    {
        root.Children.Add(new TextBlock { Text = "Шаг слайдеров", FontWeight = Avalonia.Media.FontWeight.Bold });
        root.Children.Add(new TextBlock
        {
            Text = "Слайдеры ниже \"прилипают\" к этому шагу — отдельно от шага скролла над иконкой трея (вкладка \"Трей\").",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        var sliderStepRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        sliderStepRow.Children.Add(new TextBlock { Text = "Шаг слайдера, %:", VerticalAlignment = VerticalAlignment.Center, Width = 150 });
        var sliderStepUpDown = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 50,
            Value = _appSettings.SliderStepPercent,
            Width = 150,
            FormatString = "0",
        };
        sliderStepUpDown.ValueChanged += (_, _) =>
        {
            var value = (int)(sliderStepUpDown.Value ?? 5);
            _appSettings.SliderStepPercent = value;
            _appSettingsStore.Save(_appSettings);
            foreach (var slider in _monitorSliders)
            {
                slider.TickFrequency = value;
            }
        };
        sliderStepRow.Children.Add(sliderStepUpDown);
        root.Children.Add(sliderStepRow);

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        root.Children.Add(new TextBlock { Text = "Все мониторы", FontWeight = Avalonia.Media.FontWeight.Bold });

        // "Все сразу" — чистый синхронизатор: сам не пишет в железо, а просто
        // двигает слайдер каждого монитора, который уже сам отвечает за запись.
        AddSliderRow(root, "Все сразу", ComputeAveragePercent(), _appSettings.SliderStepPercent, percent =>
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
            var slider = AddSliderRow(root, MonitorLabel.Format(monitor), current, _appSettings.SliderStepPercent, percent =>
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

    private void BuildTrayTab(StackPanel root)
    {
        root.Children.Add(new TextBlock { Text = "Скролл над иконкой трея", FontWeight = Avalonia.Media.FontWeight.Bold });

        var enabledCheckBox = new CheckBox { Content = "Включить скролл над иконкой трея", IsChecked = _traySettings.IsScrollEnabled };
        enabledCheckBox.IsCheckedChanged += (_, _) =>
        {
            _traySettings.IsScrollEnabled = enabledCheckBox.IsChecked ?? true;
            _traySettingsStore.Save(_traySettings);
        };
        root.Children.Add(enabledCheckBox);

        var stepRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        stepRow.Children.Add(new TextBlock { Text = "Шаг скролла, %:", VerticalAlignment = VerticalAlignment.Center, Width = 150 });
        var stepUpDown = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 50,
            Value = _traySettings.ScrollStepPercent,
            Width = 150,
            FormatString = "0",
        };
        stepUpDown.ValueChanged += (_, _) =>
        {
            _traySettings.ScrollStepPercent = (int)(stepUpDown.Value ?? 10);
            _traySettingsStore.Save(_traySettings);
        };
        stepRow.Children.Add(stepUpDown);
        root.Children.Add(stepRow);

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        root.Children.Add(new TextBlock { Text = "\"Липкие\" значения", FontWeight = Avalonia.Media.FontWeight.Bold });
        root.Children.Add(new TextBlock
        {
            Text = "При скролле яркость на этих значениях ненадолго задерживается (один щелчок " +
                   "колеса), чтобы легко было попасть точно в них. Остальные проценты " +
                   "по-прежнему доступны без ограничений.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        var stickyListPanel = new StackPanel { Spacing = 6 };

        void RefreshStickyList()
        {
            stickyListPanel.Children.Clear();
            foreach (var value in _traySettings.StickyValues.OrderBy(v => v).ToList())
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                row.Children.Add(new TextBlock { Text = $"{value}%", Width = 60, VerticalAlignment = VerticalAlignment.Center });

                var removeButton = new Button { Content = "Удалить" };
                removeButton.Click += (_, _) =>
                {
                    _traySettings.StickyValues.Remove(value);
                    _traySettingsStore.Save(_traySettings);
                    RefreshStickyList();
                };
                row.Children.Add(removeButton);

                stickyListPanel.Children.Add(row);
            }

            if (_traySettings.StickyValues.Count == 0)
            {
                stickyListPanel.Children.Add(new TextBlock { Text = "(пока не задано ни одного значения)", FontStyle = Avalonia.Media.FontStyle.Italic });
            }
        }

        RefreshStickyList();
        root.Children.Add(stickyListPanel);

        var addLabel = new TextBlock { Text = "Новое значение, %:", VerticalAlignment = VerticalAlignment.Center };
        var addValueInput = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 50, Width = 150, FormatString = "0" };
        var addButton = new Button { Content = "Добавить как липкое" };
        addButton.Click += (_, _) =>
        {
            var value = (int)(addValueInput.Value ?? 50);
            if (!_traySettings.StickyValues.Contains(value))
            {
                _traySettings.StickyValues.Add(value);
                _traySettingsStore.Save(_traySettings);
                RefreshStickyList();
            }
        };
        root.Children.Add(addLabel);
        root.Children.Add(addValueInput);
        root.Children.Add(addButton);
    }

    private void BuildAppearanceTab(StackPanel root)
    {
        root.Children.Add(new TextBlock { Text = "Тема", FontWeight = Avalonia.Media.FontWeight.Bold });
        root.Children.Add(BuildThemeSelector());
    }

    private void BuildScheduleTab(StackPanel root)
    {
        var scheduleStore = new JsonFileScheduleStore();
        var scheduleSettings = scheduleStore.Load();

        var enabledCheckBox = new CheckBox { Content = "Включить расписание", IsChecked = scheduleSettings.IsEnabled };
        root.Children.Add(enabledCheckBox);

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        root.Children.Add(new TextBlock { Text = "Сейчас активно", FontWeight = Avalonia.Media.FontWeight.Bold });
        var activeNowPanel = new StackPanel { Spacing = 2 };
        root.Children.Add(activeNowPanel);

        void RefreshActiveNow()
        {
            activeNowPanel.Children.Clear();
            var current = scheduleStore.Load();

            if (!current.IsEnabled || current.Rules.Count == 0)
            {
                activeNowPanel.Children.Add(new TextBlock
                {
                    Text = "Расписание выключено или правил ещё нет.",
                    FontStyle = Avalonia.Media.FontStyle.Italic,
                });
                return;
            }

            var orderedRules = current.Rules.OrderBy(r => r.Time).ToList();
            var timeOfDay = TimeOnly.FromDateTime(DateTime.Now);

            foreach (var monitor in _controller.Monitors)
            {
                var monitorKey = BrightnessController.GetMonitorKey(monitor);
                var applicable = orderedRules
                    .Where(r => r.MonitorKeys.Count == 0 || r.MonitorKeys.Contains(monitorKey))
                    .ToList();

                if (applicable.Count == 0)
                {
                    continue;
                }

                var active = ScheduleEngine.FindActiveRule(applicable, timeOfDay);
                activeNowPanel.Children.Add(new TextBlock
                {
                    Text = $"{MonitorLabel.Format(monitor)}: {active.Time:HH:mm} → {active.Percent}%",
                });
            }

            if (activeNowPanel.Children.Count == 0)
            {
                activeNowPanel.Children.Add(new TextBlock
                {
                    Text = "Ни для одного монитора нет применимых правил.",
                    FontStyle = Avalonia.Media.FontStyle.Italic,
                });
            }
        }

        enabledCheckBox.IsCheckedChanged += (_, _) =>
        {
            scheduleSettings.IsEnabled = enabledCheckBox.IsChecked ?? true;
            scheduleStore.Save(scheduleSettings);
            RefreshActiveNow();
        };

        RefreshActiveNow();
        var activeNowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        activeNowTimer.Tick += (_, _) => RefreshActiveNow();
        activeNowTimer.Start();

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        root.Children.Add(new TextBlock { Text = "Правила", FontWeight = Avalonia.Media.FontWeight.Bold });
        var rulesListPanel = new StackPanel { Spacing = 6 };

        void RefreshRulesList()
        {
            rulesListPanel.Children.Clear();

            foreach (var rule in scheduleSettings.Rules.OrderBy(r => r.Time).ToList())
            {
                var scopeText = rule.MonitorKeys.Count == 0
                    ? "все мониторы"
                    : string.Join(", ", rule.MonitorKeys.Select(key =>
                        _controller.Monitors.FirstOrDefault(m => BrightnessController.GetMonitorKey(m) == key) is { } found
                            ? MonitorLabel.Format(found)
                            : key));

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
                var timeText = new TextBlock { Text = $"{rule.Time:HH:mm}", Width = 60, VerticalAlignment = VerticalAlignment.Center };
                var percentText = new TextBlock { Text = $"{rule.Percent}%", Width = 50, VerticalAlignment = VerticalAlignment.Center };
                var scopeLabel = new TextBlock { Text = scopeText, VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                var removeButton = new Button { Content = "Удалить" };
                removeButton.Click += (_, _) =>
                {
                    scheduleSettings.Rules.Remove(rule);
                    scheduleStore.Save(scheduleSettings);
                    RefreshRulesList();
                    RefreshActiveNow();
                };

                Grid.SetColumn(timeText, 0);
                Grid.SetColumn(percentText, 1);
                Grid.SetColumn(scopeLabel, 2);
                Grid.SetColumn(removeButton, 3);
                row.Children.Add(timeText);
                row.Children.Add(percentText);
                row.Children.Add(scopeLabel);
                row.Children.Add(removeButton);

                rulesListPanel.Children.Add(row);
            }

            if (scheduleSettings.Rules.Count == 0)
            {
                rulesListPanel.Children.Add(new TextBlock { Text = "(правил ещё нет)", FontStyle = Avalonia.Media.FontStyle.Italic });
            }
        }

        RefreshRulesList();
        root.Children.Add(rulesListPanel);

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        root.Children.Add(new TextBlock { Text = "Новое правило", FontWeight = Avalonia.Media.FontWeight.Bold });

        var timePicker = new TimePicker { SelectedTime = new TimeSpan(8, 0, 0) };
        root.Children.Add(timePicker);

        var percentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        percentRow.Children.Add(new TextBlock { Text = "Яркость, %:", VerticalAlignment = VerticalAlignment.Center, Width = 150 });
        var percentInput = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 80, Width = 150, FormatString = "0" };
        percentRow.Children.Add(percentInput);
        root.Children.Add(percentRow);

        var allMonitorsCheckBox = new CheckBox { Content = "Все мониторы", IsChecked = true };
        root.Children.Add(allMonitorsCheckBox);

        var monitorCheckBoxes = new List<(MonitorInfo Monitor, CheckBox CheckBox)>();
        var monitorsPickPanel = new StackPanel { Spacing = 4, IsVisible = false };
        foreach (var monitor in _controller.Monitors)
        {
            var checkBox = new CheckBox { Content = MonitorLabel.Format(monitor) };
            monitorCheckBoxes.Add((monitor, checkBox));
            monitorsPickPanel.Children.Add(checkBox);
        }

        allMonitorsCheckBox.IsCheckedChanged += (_, _) =>
        {
            monitorsPickPanel.IsVisible = allMonitorsCheckBox.IsChecked != true;
        };
        root.Children.Add(monitorsPickPanel);

        var addRuleButton = new Button { Content = "Добавить правило" };
        addRuleButton.Click += (_, _) =>
        {
            var time = timePicker.SelectedTime ?? new TimeSpan(8, 0, 0);
            var scopeKeys = allMonitorsCheckBox.IsChecked == true
                ? new List<string>()
                : monitorCheckBoxes.Where(t => t.CheckBox.IsChecked == true).Select(t => BrightnessController.GetMonitorKey(t.Monitor)).ToList();

            scheduleSettings.Rules.Add(new ScheduleRule
            {
                Time = TimeOnly.FromTimeSpan(time),
                Percent = (int)(percentInput.Value ?? 80),
                MonitorKeys = scopeKeys,
            });
            scheduleStore.Save(scheduleSettings);
            RefreshRulesList();
            RefreshActiveNow();
        };
        root.Children.Add(addRuleButton);
    }

    private ComboBox BuildThemeSelector()
    {
        var options = new (AppThemePreference Value, string Label)[]
        {
            (AppThemePreference.System, "Системная"),
            (AppThemePreference.Light, "Светлая"),
            (AppThemePreference.Dark, "Тёмная"),
        };

        var comboBox = new ComboBox
        {
            ItemsSource = options.Select(o => o.Label).ToList(),
            SelectedIndex = Array.FindIndex(options, o => o.Value == _appSettings.Theme),
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
            _appSettings.Theme = selected;
            _appSettingsStore.Save(_appSettings);
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

    // Слайдер "прилипает" к настраиваемому шагу (см. "Шаг слайдеров" выше), а
    // кнопки ± дают точную подстройку на 1% в обход прилипания — например, до 29
    // удобнее дойти кнопкой от 30, чем медленно тащить слайдер между тиками.
    //
    // Во время реального перетаскивания слайдер пересекает много тиков подряд —
    // если писать в железо на КАЖДЫЙ тик, капризные DDC/CI-мониторы (см. FP1/FP2)
    // захлёбываются частыми командами и перестают отвечать. Поэтому во время
    // перетаскивания меняется только подпись (live), а в железо значение
    // применяется один раз — по отпусканию кнопки мыши, по клику ±, или когда
    // слайдер двигает не пользователь напрямую (например, синхронизация от
    // общего слайдера "Все сразу").
    private static Slider AddSliderRow(StackPanel root, string label, int initialPercent, int tickStep, Action<int> onChanged)
    {
        var header = new TextBlock { Text = $"{label}: {initialPercent}%" };
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = initialPercent,
            TickFrequency = tickStep,
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
