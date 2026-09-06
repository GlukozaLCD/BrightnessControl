using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BrightnessControl.App.Services;
using BrightnessControl.Core;
using System.Diagnostics;

namespace BrightnessControl.App.Views;

public partial class SettingsWindow : Window
{
    private readonly BrightnessController _controller;
    private readonly AppSettings _appSettings;
    private readonly AppSettingsStore _appSettingsStore;
    private readonly TraySettings _traySettings;
    private readonly TraySettingsStore _traySettingsStore;
    private readonly AppProfileEngine? _appProfileEngine;
    private readonly IdleEngine? _idleEngine;
    private readonly Action _onExitRequested;

    // Двухуровневая навигация (FP9 Фаза 3): _selectedCategory == null — показан
    // список категорий верхнего уровня; иначе — подкатегории ВЫБРАННОЙ категории
    // (список целиком подменяется, а не разворачивается на месте — решено заранее).
    private List<NavCategory> _rootCategories = new();
    private NavCategory? _selectedCategory;
    private bool _navOnRight = true;

    private sealed record NavCategory(string Title, List<NavSubcategory> Subcategories);
    private sealed record NavSubcategory(string Title, Action<StackPanel> BuildContent);

    // Нужен только для XAML-дизайнера/превью — реальный экземпляр всегда создаётся
    // через конструктор ниже, с реальными зависимостями.
    public SettingsWindow()
    {
        _controller = null!;
        _appSettings = null!;
        _appSettingsStore = null!;
        _traySettings = null!;
        _traySettingsStore = null!;
        _appProfileEngine = null;
        _idleEngine = null;
        _onExitRequested = () => { };
        InitializeComponent();
    }

    public SettingsWindow(
        BrightnessController controller,
        AppSettings appSettings,
        AppSettingsStore appSettingsStore,
        TraySettings traySettings,
        TraySettingsStore traySettingsStore,
        AppProfileEngine? appProfileEngine,
        IdleEngine? idleEngine,
        Action onExitRequested)
    {
        _controller = controller;
        _appSettings = appSettings;
        _appSettingsStore = appSettingsStore;
        _traySettings = traySettings;
        _traySettingsStore = traySettingsStore;
        _appProfileEngine = appProfileEngine;
        _idleEngine = idleEngine;
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

        // Случайный клик закрывает всё приложение (не просто окно настроек) — легко
        // промахнуться мимо более безобидной цели. Требуем зажатый Shift как
        // защиту от случайного закрытия.
        ToolTip.SetTip(closeButton, "Удерживайте Shift и кликните, чтобы выйти из программы");

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
        closeButton.PointerPressed += (_, e) =>
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _onExitRequested();
            }
            else
            {
                // Обычный клик — просто скрывает окно настроек, как и клик мимо.
                Close();
            }
        };
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
        _rootCategories = new List<NavCategory>
        {
            new("Настройки", new List<NavSubcategory>
            {
                new("Слайдеры яркости", BuildSliderStepTab),
                new("Ярлык трея", BuildTrayTab),
                new("Оформление", BuildAppearanceTab),
            }),
            new("Автоматизация", new List<NavSubcategory>
            {
                new("Расписание", BuildScheduleTab),
                new("Профили приложений", BuildAppProfilesTab),
                new("Простой", BuildIdleTab),
            }),
        };

        SetupNavFlip();
        RenderNavList();
        SelectSubcategory(_rootCategories[0].Subcategories[0]);
    }

    // Кнопка сверху списка перекидывает сам список категорий между правым и левым
    // краем окна — стрелка всегда показывает, КУДА он поедет по клику (не где он
    // сейчас), поэтому разворачивается в противоположную сторону при каждом клике.
    private void SetupNavFlip()
    {
        var grid = this.FindControl<Grid>("NavContentGrid")!;
        var navBorder = this.FindControl<Border>("NavBorder")!;
        var contentScroll = (Control)grid.Children.First(c => c is ScrollViewer);
        var flipButton = this.FindControl<Button>("NavFlipButton")!;

        flipButton.Click += (_, _) =>
        {
            _navOnRight = !_navOnRight;

            // Раньше менялся только Grid.Column у детей, а сами ColumnDefinitions
            // оставались "*,Auto" всегда — при переносе налево список попадал в
            // "резиновую" звёздочную колонку вместо колонки под свой фиксированный
            // размер и не прижимался к углу, а "плавал". Колонки нужно переставлять
            // местами вместе с детьми, а не только менять им индекс.
            grid.ColumnDefinitions = _navOnRight
                ? new ColumnDefinitions("*,Auto")
                : new ColumnDefinitions("Auto,*");
            Grid.SetColumn(navBorder, _navOnRight ? 1 : 0);
            Grid.SetColumn(contentScroll, _navOnRight ? 0 : 1);
            navBorder.HorizontalAlignment = _navOnRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            flipButton.Content = _navOnRight ? "←" : "→";
        };
    }

    private void RenderNavList()
    {
        var navList = this.FindControl<StackPanel>("NavList")!;
        navList.Children.Clear();

        if (_selectedCategory is null)
        {
            foreach (var category in _rootCategories)
            {
                var button = new Button
                {
                    Content = category.Title,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                button.Click += (_, _) =>
                {
                    _selectedCategory = category;
                    RenderNavList();
                    SelectSubcategory(category.Subcategories[0]);
                };
                navList.Children.Add(button);
            }

            return;
        }

        var backButton = new Button
        {
            Content = "← Назад",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        backButton.Click += (_, _) =>
        {
            _selectedCategory = null;
            RenderNavList();
        };
        navList.Children.Add(backButton);
        navList.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 4) });

        foreach (var subcategory in _selectedCategory.Subcategories)
        {
            var button = new Button
            {
                Content = subcategory.Title,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            button.Click += (_, _) => SelectSubcategory(subcategory);
            navList.Children.Add(button);
        }
    }

    private void SelectSubcategory(NavSubcategory subcategory)
    {
        var contentPanel = this.FindControl<StackPanel>("ContentPanel")!;
        contentPanel.Children.Clear();
        subcategory.BuildContent(contentPanel);
    }

    // Всё, что осталось от бывшей вкладки "Мониторы" — сами слайдеры яркости
    // переехали в поповер трея по левому клику (FP9 Фаза 2, GlobalSliderPopup).
    private void BuildSliderStepTab(StackPanel root)
    {
        var sliderStepHeader = new TextBlock { Text = "Шаг слайдеров", FontWeight = Avalonia.Media.FontWeight.Bold };
        ToolTip.SetTip(sliderStepHeader, "Слайдеры в поповере трея (левый клик по иконке) \"прилипают\" к этому шагу — отдельно от шага скролла над иконкой трея (см. \"Ярлык трея\").");
        root.Children.Add(sliderStepHeader);

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
        };
        sliderStepRow.Children.Add(sliderStepUpDown);
        root.Children.Add(sliderStepRow);
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
        var stickyHeader = new TextBlock { Text = "\"Липкие\" значения", FontWeight = Avalonia.Media.FontWeight.Bold };
        ToolTip.SetTip(stickyHeader, "При скролле яркость на этих значениях ненадолго задерживается (один щелчок " +
            "колеса), чтобы легко было попасть точно в них. Остальные проценты по-прежнему доступны без ограничений.");
        root.Children.Add(stickyHeader);

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

        // Заголовки колонок — раньше их не было, и сразу не было понятно, что
        // означает каждое число в строке правила (FP9 Фаза 5: форма расписания
        // была "странной и непонятной").
        var rulesHeaderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("60,50,*,Auto") };
        var rulesHeaderStyle = new Action<TextBlock>(t => t.FontWeight = Avalonia.Media.FontWeight.Bold);
        var timeHeader = new TextBlock { Text = "Время" };
        var percentHeader = new TextBlock { Text = "Яркость" };
        var scopeHeader = new TextBlock { Text = "Мониторы" };
        rulesHeaderStyle(timeHeader);
        rulesHeaderStyle(percentHeader);
        rulesHeaderStyle(scopeHeader);
        Grid.SetColumn(timeHeader, 0);
        Grid.SetColumn(percentHeader, 1);
        Grid.SetColumn(scopeHeader, 2);
        rulesHeaderRow.Children.Add(timeHeader);
        rulesHeaderRow.Children.Add(percentHeader);
        rulesHeaderRow.Children.Add(scopeHeader);
        root.Children.Add(rulesHeaderRow);

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

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("60,50,*,Auto") };
                var timeText = new TextBlock { Text = $"{rule.Time:HH:mm}", VerticalAlignment = VerticalAlignment.Center };
                var percentText = new TextBlock { Text = $"{rule.Percent}%", VerticalAlignment = VerticalAlignment.Center };
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

        // Визуально отделённая карточка для формы создания правила — раньше поля
        // шли сплошным списком без границ, сливаясь со списком уже существующих
        // правил выше (FP9 Фаза 5).
        var newRuleCard = new Border
        {
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
        };
        var newRulePanel = new StackPanel { Spacing = 8 };
        newRuleCard.Child = newRulePanel;

        newRulePanel.Children.Add(new TextBlock { Text = "Новое правило", FontWeight = Avalonia.Media.FontWeight.Bold });

        // Крупные читаемые цифры "12:40" вместо NumericUpDown (тот оказался слишком
        // узким — виден был почти только край со стрелочками, а не само число).
        // Прокрутка колеса над часом/минутой — ±1 к целому числу; с зажатым Shift —
        // точнее: над десятками ±10, над единицами ±1 (см. BuildScrollableTwoDigit).
        var hour = 8;
        var minute = 0;
        var hourControl = BuildScrollableTwoDigit(() => hour, v => hour = v, 0, 23);
        var minuteControl = BuildScrollableTwoDigit(() => minute, v => minute = v, 0, 59);

        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        timeRow.Children.Add(new TextBlock { Text = "Время:", VerticalAlignment = VerticalAlignment.Center, Width = 150 });
        timeRow.Children.Add(hourControl);
        timeRow.Children.Add(new TextBlock { Text = ":", VerticalAlignment = VerticalAlignment.Center, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
        timeRow.Children.Add(minuteControl);
        newRulePanel.Children.Add(timeRow);

        var percentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        percentRow.Children.Add(new TextBlock { Text = "Яркость, %:", VerticalAlignment = VerticalAlignment.Center, Width = 150 });
        var percentInput = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 80, Width = 150, FormatString = "0" };
        percentRow.Children.Add(percentInput);
        newRulePanel.Children.Add(percentRow);

        var allMonitorsCheckBox = new CheckBox { Content = "Все мониторы", IsChecked = true };
        newRulePanel.Children.Add(allMonitorsCheckBox);

        // Список мониторов теперь виден ВСЕГДА (раньше прятался, пока не снять
        // "Все мониторы" — не было понятно, что выбор вообще есть). Чекбокс "Все
        // мониторы" просто отмечает/блокирует остальные, а не скрывает список.
        var monitorCheckBoxes = new List<(MonitorInfo Monitor, CheckBox CheckBox)>();
        var monitorsPickPanel = new StackPanel { Spacing = 4, Margin = new Thickness(20, 0, 0, 0) };
        foreach (var monitor in _controller.Monitors)
        {
            var checkBox = new CheckBox { Content = MonitorLabel.Format(monitor), IsChecked = true, IsEnabled = false };
            monitorCheckBoxes.Add((monitor, checkBox));
            monitorsPickPanel.Children.Add(checkBox);
        }

        allMonitorsCheckBox.IsCheckedChanged += (_, _) =>
        {
            var allSelected = allMonitorsCheckBox.IsChecked == true;
            foreach (var (_, checkBox) in monitorCheckBoxes)
            {
                checkBox.IsEnabled = !allSelected;
                if (allSelected)
                {
                    checkBox.IsChecked = true;
                }
            }
        };
        newRulePanel.Children.Add(monitorsPickPanel);

        var addRuleButton = new Button { Content = "Добавить правило" };
        addRuleButton.Click += (_, _) =>
        {
            var time = new TimeSpan(hour, minute, 0);
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
        newRulePanel.Children.Add(addRuleButton);

        root.Children.Add(newRuleCard);
    }

    private void BuildAppProfilesTab(StackPanel root)
    {
        var profileStore = new JsonFileAppProfileStore();
        var profileSettings = profileStore.Load();

        var enabledCheckBox = new CheckBox { Content = "Включить профили приложений", IsChecked = profileSettings.IsEnabled };
        root.Children.Add(enabledCheckBox);

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        root.Children.Add(new TextBlock { Text = "Сейчас активно", FontWeight = Avalonia.Media.FontWeight.Bold });
        var activeNowPanel = new StackPanel { Spacing = 2 };
        root.Children.Add(activeNowPanel);

        void RefreshActiveNow()
        {
            activeNowPanel.Children.Clear();
            var current = profileStore.Load();
            var activeProfileId = _appProfileEngine?.ActiveProfileId;
            var activeMonitorAdapterName = _appProfileEngine?.ActiveMonitorAdapterDeviceName;

            if (!current.IsEnabled)
            {
                activeNowPanel.Children.Add(new TextBlock { Text = "Профили выключены.", FontStyle = Avalonia.Media.FontStyle.Italic });
                return;
            }

            if (activeProfileId is null)
            {
                activeNowPanel.Children.Add(new TextBlock { Text = "Сейчас ни один профиль не активен.", FontStyle = Avalonia.Media.FontStyle.Italic });
                return;
            }

            var activeProfile = current.Profiles.FirstOrDefault(p => p.Id == activeProfileId);
            var monitor = _controller.Monitors.FirstOrDefault(m => m.AdapterDeviceName == activeMonitorAdapterName);
            var monitorLabel = monitor is not null ? MonitorLabel.Format(monitor) : activeMonitorAdapterName ?? "?";

            activeNowPanel.Children.Add(new TextBlock
            {
                Text = activeProfile is not null
                    ? $"«{activeProfile.MatchValue}» → {activeProfile.Percent}% на {monitorLabel}"
                    : $"Профиль {activeProfileId} на {monitorLabel}",
            });
        }

        enabledCheckBox.IsCheckedChanged += (_, _) =>
        {
            profileSettings.IsEnabled = enabledCheckBox.IsChecked ?? true;
            profileStore.Save(profileSettings);
            RefreshActiveNow();
        };

        RefreshActiveNow();
        var activeNowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        activeNowTimer.Tick += (_, _) => RefreshActiveNow();
        activeNowTimer.Start();

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        root.Children.Add(new TextBlock { Text = "Профили", FontWeight = Avalonia.Media.FontWeight.Bold });
        var profilesListPanel = new StackPanel { Spacing = 6 };

        void RefreshProfilesList()
        {
            profilesListPanel.Children.Clear();

            foreach (var profile in profileSettings.Profiles)
            {
                var matchTypeText = profile.MatchType == AppMatchType.ProcessName ? "процесс" : "заголовок";

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
                var typeText = new TextBlock { Text = matchTypeText, Width = 70, VerticalAlignment = VerticalAlignment.Center };
                var valueText = new TextBlock { Text = profile.MatchValue, VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                var percentText = new TextBlock { Text = $"{profile.Percent}%", Width = 50, VerticalAlignment = VerticalAlignment.Center };
                var removeButton = new Button { Content = "Удалить" };
                removeButton.Click += (_, _) =>
                {
                    profileSettings.Profiles.Remove(profile);
                    profileStore.Save(profileSettings);
                    RefreshProfilesList();
                };

                Grid.SetColumn(typeText, 0);
                Grid.SetColumn(valueText, 1);
                Grid.SetColumn(percentText, 2);
                Grid.SetColumn(removeButton, 3);
                row.Children.Add(typeText);
                row.Children.Add(valueText);
                row.Children.Add(percentText);
                row.Children.Add(removeButton);

                profilesListPanel.Children.Add(row);
            }

            if (profileSettings.Profiles.Count == 0)
            {
                profilesListPanel.Children.Add(new TextBlock { Text = "(профилей ещё нет)", FontStyle = Avalonia.Media.FontStyle.Italic });
            }
        }

        RefreshProfilesList();
        root.Children.Add(profilesListPanel);

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        root.Children.Add(new TextBlock { Text = "Новый профиль", FontWeight = Avalonia.Media.FontWeight.Bold });

        var matchTypeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        matchTypeRow.Children.Add(new TextBlock { Text = "Определять по:", VerticalAlignment = VerticalAlignment.Center, Width = 150 });
        var matchTypeCombo = new ComboBox
        {
            ItemsSource = new[] { "Запущенный процесс (из списка)", "Заголовок окна (текст)" },
            SelectedIndex = 0,
            MinWidth = 220,
        };
        matchTypeRow.Children.Add(matchTypeCombo);
        root.Children.Add(matchTypeRow);

        var processPickRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        // Все переменные, на которые ссылаются локальные функции/шаблоны ниже, объявлены
        // здесь заранее (просто как объекты, без ItemTemplate) — иначе анализ определённого
        // присваивания C# ругается, даже если реально эти обработчики выполнятся значительно позже.
        var processAutoComplete = new AutoCompleteBox
        {
            MinWidth = 260,
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0,
        };
        var hiddenProcessesLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var hiddenProcessesRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var hiddenProcessesCombo = new ComboBox { MinWidth = 200 };

        // Текст "Скрыто процессов: N" — отдельный TextBlock рядом с ComboBox, а не его
        // PlaceholderText: раньше рядом с этой надписью был виден плюсик — ComboBox в
        // закрытом состоянии иногда показывал шаблон первого пункта вместо плейсхолдера.
        // Вынос текста наружу и принудительный сброс выбора (см. SelectionChanged ниже)
        // полностью убирают этот эффект.
        hiddenProcessesCombo.SelectionChanged += (_, _) =>
        {
            if (hiddenProcessesCombo.SelectedIndex != -1)
            {
                hiddenProcessesCombo.SelectedIndex = -1;
            }
        };

        // Первый пункт списка — не процесс, а спец-строка "вернуть все сразу"
        // (жирный текст + крупный плюс, кликабельна целиком). Ниже неё —
        // обычные пункты по одному процессу с плюсом, для точечного возврата.
        // Значение — случайный GUID, гарантированно не совпадёт с реальным именем процесса.
        var restoreAllSentinel = Guid.NewGuid().ToString();

        hiddenProcessesCombo.ItemTemplate = new FuncDataTemplate<string>((name, _) =>
        {
            if (name == restoreAllSentinel)
            {
                var allRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(2) };
                var allText = new TextBlock
                {
                    Text = "Вернуть все скрытые процессы",
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var restoreAllGlyph = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(12),
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x2E, 0x8B, 0x3D)),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                    Child = new TextBlock
                    {
                        Text = "+",
                        Foreground = Avalonia.Media.Brushes.White,
                        FontSize = 16,
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                allRow.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    RestoreAllProcessNames();
                };

                Grid.SetColumn(allText, 0);
                Grid.SetColumn(restoreAllGlyph, 1);
                allRow.Children.Add(allText);
                allRow.Children.Add(restoreAllGlyph);
                return allRow;
            }

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(2) };
            var text = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };

            var restoreGlyph = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3C, 0xA0, 0x50)),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = "+",
                    Foreground = Avalonia.Media.Brushes.White,
                    FontSize = 13,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            restoreGlyph.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                RestoreProcessName(name);
            };

            Grid.SetColumn(text, 0);
            Grid.SetColumn(restoreGlyph, 1);
            row.Children.Add(text);
            row.Children.Add(restoreGlyph);
            return row;
        });

        void RestoreProcessName(string name)
        {
            _appSettings.HiddenProcessNames.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            _appSettingsStore.Save(_appSettings);
            RefreshRunningProcesses();
            RefreshHiddenLink();
        }

        void RestoreAllProcessNames()
        {
            _appSettings.HiddenProcessNames.Clear();
            _appSettingsStore.Save(_appSettings);
            RefreshRunningProcesses();
            RefreshHiddenLink();
        }

        void RefreshHiddenLink()
        {
            var hiddenNames = _appSettings.HiddenProcessNames.OrderBy(n => n).ToList();
            hiddenProcessesRow.IsVisible = hiddenNames.Count > 0;
            hiddenProcessesCombo.ItemsSource = hiddenNames.Count > 0
                ? new List<string> { restoreAllSentinel }.Concat(hiddenNames).ToList()
                : hiddenNames;
            hiddenProcessesLabel.Text = $"Скрыто процессов: {hiddenNames.Count}";
            hiddenProcessesCombo.SelectedIndex = -1;
        }

        // Каждый пункт списка — имя процесса + серый кружок с "−" для скрытия
        // ненужных процессов из подсказок (например, служебных). Скрытые запоминаются
        // в AppSettings.HiddenProcessNames и не показываются, пока их явно не вернуть.
        processAutoComplete.ItemTemplate = new FuncDataTemplate<string>((name, _) =>
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(2) };
            var text = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };

            var hideGlyph = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x90, 0x90, 0x90)),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = "−",
                    Foreground = Avalonia.Media.Brushes.White,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            hideGlyph.PointerPressed += (_, e) =>
            {
                // Иначе клик по крестику также сработал бы как выбор всего пункта списка.
                e.Handled = true;
                HideProcessName(name);
            };

            Grid.SetColumn(text, 0);
            Grid.SetColumn(hideGlyph, 1);
            row.Children.Add(text);
            row.Children.Add(hideGlyph);
            return row;
        });

        var openAllButton = new Button { Content = "▼", Width = 32 };
        ToolTip.SetTip(openAllButton, "Показать список всех запущенных процессов");
        var refreshProcessesButton = new Button { Content = "Обновить список" };

        void RefreshRunningProcesses()
        {
            var hidden = new HashSet<string>(_appSettings.HiddenProcessNames, StringComparer.OrdinalIgnoreCase);
            var names = Process.GetProcesses()
                .Where(p => p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle))
                .Select(p => p.ProcessName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => !hidden.Contains(n))
                .OrderBy(n => n)
                .ToList();

            processAutoComplete.ItemsSource = names;
        }

        void HideProcessName(string name)
        {
            if (!_appSettings.HiddenProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                _appSettings.HiddenProcessNames.Add(name);
                _appSettingsStore.Save(_appSettings);
            }

            RefreshRunningProcesses();
            RefreshHiddenLink();
        }

        openAllButton.Click += (_, _) =>
        {
            RefreshRunningProcesses();
            processAutoComplete.IsDropDownOpen = true;
        };
        refreshProcessesButton.Click += (_, _) => RefreshRunningProcesses();
        RefreshRunningProcesses();
        processPickRow.Children.Add(processAutoComplete);
        processPickRow.Children.Add(openAllButton);
        processPickRow.Children.Add(refreshProcessesButton);
        root.Children.Add(processPickRow);

        hiddenProcessesRow.Children.Add(hiddenProcessesLabel);
        hiddenProcessesRow.Children.Add(hiddenProcessesCombo);
        root.Children.Add(hiddenProcessesRow);
        RefreshHiddenLink();

        var titleValueRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, IsVisible = false };
        titleValueRow.Children.Add(new TextBlock { Text = "Часть заголовка окна:", VerticalAlignment = VerticalAlignment.Center, Width = 150 });
        var titleValueInput = new TextBox { Width = 260 };
        titleValueRow.Children.Add(titleValueInput);
        root.Children.Add(titleValueRow);

        matchTypeCombo.SelectionChanged += (_, _) =>
        {
            var isProcess = matchTypeCombo.SelectedIndex == 0;
            processPickRow.IsVisible = isProcess;
            titleValueRow.IsVisible = !isProcess;
        };

        var percentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        percentRow.Children.Add(new TextBlock { Text = "Яркость, %:", VerticalAlignment = VerticalAlignment.Center, Width = 150 });
        var percentInput = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 50, Width = 150, FormatString = "0" };
        percentRow.Children.Add(percentInput);
        root.Children.Add(percentRow);

        var addProfileButton = new Button { Content = "Добавить профиль" };
        addProfileButton.Click += (_, _) =>
        {
            var isProcess = matchTypeCombo.SelectedIndex == 0;
            var matchValue = isProcess
                ? processAutoComplete.Text
                : titleValueInput.Text;

            if (string.IsNullOrWhiteSpace(matchValue))
            {
                return;
            }

            profileSettings.Profiles.Add(new AppProfile
            {
                MatchType = isProcess ? AppMatchType.ProcessName : AppMatchType.WindowTitle,
                MatchValue = matchValue.Trim(),
                Percent = (int)(percentInput.Value ?? 50),
            });
            profileStore.Save(profileSettings);
            RefreshProfilesList();
        };
        root.Children.Add(addProfileButton);
    }

    private void BuildIdleTab(StackPanel root)
    {
        var idleStore = new JsonFileIdleSettingsStore();
        var idleSettings = idleStore.Load();

        var enabledCheckBox = new CheckBox { Content = "Включить приглушение по бездействию", IsChecked = idleSettings.IsEnabled };
        ToolTip.SetTip(enabledCheckBox, "Приглушает яркость ВСЕХ мониторов разом после N минут без клавиатуры/мыши " +
            "и восстанавливает при возврате активности (актуальное значение расписания или профиля приложения, если применимо — не устаревший снимок).");
        root.Children.Add(enabledCheckBox);

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });

        var timeoutRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        timeoutRow.Children.Add(new TextBlock { Text = "Таймаут простоя, мин:", VerticalAlignment = VerticalAlignment.Center, Width = 180 });
        var timeoutInput = new NumericUpDown { Minimum = 1, Maximum = 180, Value = idleSettings.IdleTimeoutMinutes, Width = 150, FormatString = "0" };
        timeoutRow.Children.Add(timeoutInput);
        root.Children.Add(timeoutRow);

        var dimPercentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        dimPercentRow.Children.Add(new TextBlock { Text = "Яркость при простое, %:", VerticalAlignment = VerticalAlignment.Center, Width = 180 });
        var dimPercentInput = new NumericUpDown { Minimum = 0, Maximum = 100, Value = idleSettings.DimPercent, Width = 150, FormatString = "0" };
        dimPercentRow.Children.Add(dimPercentInput);
        root.Children.Add(dimPercentRow);

        var pollIntervalRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var pollIntervalLabel = new TextBlock { Text = "Проверка простоя, сек:", VerticalAlignment = VerticalAlignment.Center, Width = 180 };
        ToolTip.SetTip(pollIntervalLabel, "Как часто проверяется, не пошевелили ли вы мышью/клавиатурой. Меньше — " +
            "отзывчивее восстановление после простоя, но чуть чаще фоновая проверка.");
        pollIntervalRow.Children.Add(pollIntervalLabel);
        var pollIntervalInput = new NumericUpDown { Minimum = 1, Maximum = 60, Value = idleSettings.PollIntervalSeconds, Width = 150, FormatString = "0" };
        pollIntervalRow.Children.Add(pollIntervalInput);
        root.Children.Add(pollIntervalRow);

        enabledCheckBox.IsCheckedChanged += (_, _) =>
        {
            idleSettings.IsEnabled = enabledCheckBox.IsChecked ?? true;
            idleStore.Save(idleSettings);
        };
        timeoutInput.ValueChanged += (_, _) =>
        {
            idleSettings.IdleTimeoutMinutes = (int)(timeoutInput.Value ?? 5);
            idleStore.Save(idleSettings);
        };
        dimPercentInput.ValueChanged += (_, _) =>
        {
            idleSettings.DimPercent = (int)(dimPercentInput.Value ?? 10);
            idleStore.Save(idleSettings);
        };
        pollIntervalInput.ValueChanged += (_, _) =>
        {
            idleSettings.PollIntervalSeconds = (int)(pollIntervalInput.Value ?? 1);
            idleStore.Save(idleSettings);
            // Иначе новый интервал подхватится только на следующем перезапуске
            // приложения — таймер уже создан со старым значением.
            _idleEngine?.Start();
        };

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        root.Children.Add(new TextBlock { Text = "Сейчас", FontWeight = Avalonia.Media.FontWeight.Bold });
        var statusText = new TextBlock();
        root.Children.Add(statusText);

        void RefreshStatus()
        {
            statusText.Text = _idleEngine?.IsDimmed == true
                ? "Приглушено по бездействию"
                : "Активно (не приглушено)";
        }

        RefreshStatus();
        var statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        statusTimer.Tick += (_, _) => RefreshStatus();
        statusTimer.Start();
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
    // Internal — переиспользуется GlobalSliderPopup (FP9 Фаза 2), не только этим окном.
    // nameColumnWidth — под длинные названия мониторов в узком поповере название
    // едет "бегущей строкой", а не обрезается; в широком окне настроек места и так
    // хватает, поэтому запас пошире и анимация практически никогда не включается.
    internal static Slider AddSliderRow(StackPanel root, string label, int initialPercent, int tickStep, Action<int> onChanged, double nameColumnWidth = 360)
    {
        var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var nameLabel = BuildMarqueeLabel(label, nameColumnWidth);
        var percentLabel = new TextBlock { Text = $"{initialPercent}%", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(nameLabel, 0);
        Grid.SetColumn(percentLabel, 1);
        headerRow.Children.Add(nameLabel);
        headerRow.Children.Add(percentLabel);

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = initialPercent,
            TickFrequency = tickStep,
            IsSnapToTickEnabled = true,
        };

        // Всплывающий пузырёк с процентом прямо над кружком слайдера — статичная
        // подпись "название: процент" не влезает в узкий поповер (см. percentLabel
        // выше — по той же причине она вынесена отдельно), а пузырёк даёт точную
        // обратную связь именно там, где палец/курсор тянет слайдер.
        //
        // Ширина/высота ФИКСИРОВАНЫ (не подстраиваются под текст) — раньше центр
        // считался через bubble.Bounds.Width, а она меняется в зависимости от
        // количества цифр (9% против 100%), из-за чего пузырёк ощутимо "шатался"
        // при перетаскивании. С фиксированным размером делитель в формуле центрирования
        // всегда один и тот же — дрожи по X больше нет.
        const double bubbleWidth = 34;
        const double bubbleBodyHeight = 20;
        const double bubbleTailSize = 10;
        const double bubbleGap = 3;
        const double bubbleTotalHeight = bubbleBodyHeight + bubbleTailSize / 2;

        var bubbleText = new TextBlock
        {
            Text = $"{initialPercent}%",
            Foreground = Avalonia.Media.Brushes.White,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var bubbleBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0xE8, 0x30, 0x30, 0x30));
        var bubbleBody = new Border
        {
            Width = bubbleWidth,
            Height = bubbleBodyHeight,
            CornerRadius = new CornerRadius(5),
            Background = bubbleBrush,
            Child = bubbleText,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        // Повёрнутый на 45° квадрат: верхняя половина спрятана ПОД телом пузырька
        // (добавлен в Panel раньше него, значит рисуется ниже по z-order), снизу
        // торчит только острый кончик — классический приём для "хвостика" подсказки,
        // конец которого всегда точно над кружком слайдера.
        var bubbleTail = new Border
        {
            Width = bubbleTailSize,
            Height = bubbleTailSize,
            Background = bubbleBrush,
            RenderTransform = new Avalonia.Media.RotateTransform(45),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness((bubbleWidth - bubbleTailSize) / 2, bubbleBodyHeight - bubbleTailSize / 2, 0, 0),
        };
        var bubble = new Panel
        {
            Width = bubbleWidth,
            Height = bubbleTotalHeight,
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        bubble.Children.Add(bubbleTail);
        bubble.Children.Add(bubbleBody);

        // Panel (не StackPanel) — чтобы пузырёк мог рисоваться поверх и НАД строкой
        // слайдера, не будучи прижатым к её собственным границам. Объявлен здесь
        // (заполнится ниже), чтобы TranslatePoint пересчитывал позицию пузырька
        // именно в его системе координат, а не в системе координат Grid со слайдером.
        var sliderHost = new Panel();

        Thumb? thumb = null;

        void RepositionBubble()
        {
            thumb ??= slider.GetVisualDescendants().OfType<Thumb>().FirstOrDefault();
            if (thumb is null)
            {
                return;
            }

            // Y=0 в системе координат самого thumb — это его верхний край; переводим
            // именно эту точку, чтобы хвостик всегда указывал строго на верх кружка,
            // а не на его центр (тогда кончик "прятался" бы внутри кружка).
            var top = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, 0), sliderHost);
            if (top is not { } point)
            {
                return;
            }

            bubble.Margin = new Thickness(point.X - bubbleWidth / 2, point.Y - bubbleGap - bubbleTotalHeight, 0, 0);
        }

        var isDragging = false;

        // Раньше пузырёк переставлялся сразу внутри обработчика PropertyChanged —
        // но на этот момент Avalonia ещё не успела ЗАНОВО РАСПОЛОЖИТЬ сам кружок
        // (Value уже новое, а Arrange кружка происходит на СЛЕДУЮЩЕМ проходе
        // layout) — из-за этого пузырёк читал СТАРУЮ позицию кружка, на шаг позади
        // реальной, и при быстром перетаскивании туда-сюда это выглядело как
        // дрожь/шатание. LayoutUpdated срабатывает уже ПОСЛЕ фактического Arrange —
        // подписка живёт, только пока пузырёк реально виден.
        void OnSliderLayoutUpdated(object? sender, EventArgs e) => RepositionBubble();

        void UpdateBubbleVisibility()
        {
            var shouldShow = isDragging || slider.IsPointerOver;
            if (shouldShow == bubble.IsVisible)
            {
                return;
            }

            bubble.IsVisible = shouldShow;
            if (shouldShow)
            {
                slider.LayoutUpdated += OnSliderLayoutUpdated;
                RepositionBubble();
            }
            else
            {
                slider.LayoutUpdated -= OnSliderLayoutUpdated;
            }
        }

        slider.PointerEntered += (_, _) => UpdateBubbleVisibility();
        slider.PointerExited += (_, _) => UpdateBubbleVisibility();

        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty)
            {
                return;
            }

            var percent = (int)slider.Value;
            percentLabel.Text = $"{percent}%";
            bubbleText.Text = $"{percent}%";

            if (!isDragging)
            {
                onChanged(percent);
            }
        };

        slider.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
        {
            isDragging = true;
            UpdateBubbleVisibility();
        }, handledEventsToo: true);
        slider.AddHandler(InputElement.PointerReleasedEvent, (_, _) =>
        {
            isDragging = false;
            onChanged((int)slider.Value);
            UpdateBubbleVisibility();
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

        sliderHost.Children.Add(row);
        sliderHost.Children.Add(bubble);

        root.Children.Add(headerRow);
        root.Children.Add(sliderHost);
        return slider;
    }

    // Название монитора едет туда-обратно бегущей строкой от начала до самого конца,
    // только если реально не помещается в отведённую ширину — короткие названия
    // остаются статичными без анимации.
    //
    // Ширина текста меряется через FormattedText (полностью отдельная, "бумажная"
    // операция) — НЕ через textBlock.Measure(...): вызов Measure() напрямую на
    // TextBlock, который уже присоединён к живому дереву и участвует в обычном
    // цикле layout, сбивает его с толку и портит реальную раскладку (ровно это и
    // сломало строку "Все мониторы" — текст начал переноситься/резаться). Сам
    // шрифт/размер для FormattedText читаются ЛЕНИВО на первом тике таймера (а не
    // сразу при создании) — до присоединения к дереву стиль темы ещё не применён,
    // и раннее чтение FontFamily/FontSize даёт метрики "по умолчанию", не совпадающие
    // с реально отрисованными (отсюда была неверная амплитуда прокрутки).
    // Два часа/минуты в форме расписания (FP9 Фаза 5) вводятся прокруткой колеса, а
    // не NumericUpDown — тот на практике оказался слишком узким, число почти не было
    // видно рядом со стрелочками. Цифры "десятки"/"единицы" — отдельные, независимо
    // наводимые TextBlock: прокрутка над ЛЮБОЙ из них по умолчанию меняет ВСЁ число
    // на ±1 (так проще и предсказуемее — не нужно целиться в конкретную цифру), а с
    // зажатым Shift — именно ту цифру, над которой курсор (десятки → ±10, единицы → ±1).
    private static Control BuildScrollableTwoDigit(Func<int> getValue, Action<int> setValue, int min, int max)
    {
        // Ширина/выравнивание ФИКСИРОВАНЫ — без этого узкие цифры ("1") и широкие
        // ("8") занимали разную ширину, весь блок "сдвигался" при каждом изменении
        // значения, курсор оставался на месте, а цифра "уезжала" из-под него — из-за
        // этого следующий скролл иногда попадал уже на ScrollViewer всего окна.
        const double digitWidth = 16;
        var tensDigit = new TextBlock
        {
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Width = digitWidth,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
        };
        var onesDigit = new TextBlock
        {
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Width = digitWidth,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
        };
        var cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeNorthSouth);
        tensDigit.Cursor = cursor;
        onesDigit.Cursor = cursor;

        void Refresh()
        {
            var text = getValue().ToString("00");
            tensDigit.Text = text[..1];
            onesDigit.Text = text[1..];
        }

        Refresh();

        void HandleWheel(int placeValue, PointerWheelEventArgs e)
        {
            // Помечаем обработанным сразу, а не только при реальном изменении —
            // иначе "пустой" (нулевой) скролл-евент может провалиться дальше и
            // прокрутить ScrollViewer всего окна настроек.
            e.Handled = true;

            var notches = Math.Sign(e.Delta.Y);
            if (notches == 0)
            {
                return;
            }

            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? placeValue : 1;
            setValue(Math.Clamp(getValue() + notches * step, min, max));
            Refresh();
        }

        tensDigit.PointerWheelChanged += (_, e) => HandleWheel(10, e);
        onesDigit.PointerWheelChanged += (_, e) => HandleWheel(1, e);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        row.Children.Add(tensDigit);
        row.Children.Add(onesDigit);

        ToolTip.SetTip(row, "Прокрутите колесо мыши: ±1 к числу. С зажатым Shift — точнее: над первой цифрой ±10, над второй ±1.");

        return row;
    }

    private static Control BuildMarqueeLabel(string text, double width)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
        };
        var clip = new Border { ClipToBounds = true, Width = width, Child = textBlock };

        var transform = new Avalonia.Media.TranslateTransform();
        textBlock.RenderTransform = transform;

        double? overflow = null;
        var forward = true;
        var pauseTicksRemaining = 0;
        const double pixelsPerTick = 1.2;
        const int pauseTicksAtEnds = 25; // ~750мс на паузу, чтобы конец/начало успевали прочитаться

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) =>
        {
            if (overflow is null)
            {
                var typeface = new Avalonia.Media.Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight);
                var formatted = new Avalonia.Media.FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    Avalonia.Media.FlowDirection.LeftToRight,
                    typeface,
                    textBlock.FontSize,
                    null);
                var measured = formatted.Width - width;
                if (measured <= 0)
                {
                    timer.Stop();
                    return;
                }

                overflow = measured;
            }

            if (pauseTicksRemaining > 0)
            {
                pauseTicksRemaining--;
                return;
            }

            var next = transform.X + (forward ? -pixelsPerTick : pixelsPerTick);
            if (next <= -overflow)
            {
                next = -overflow.Value;
                forward = false;
                pauseTicksRemaining = pauseTicksAtEnds;
            }
            else if (next >= 0)
            {
                next = 0;
                forward = true;
                pauseTicksRemaining = pauseTicksAtEnds;
            }

            transform.X = next;
        };
        timer.Start();

        // Иначе таймер продолжит тикать вечно в фоне после закрытия окна —
        // строка больше не в дереве, значения меняются, но их никто не видит.
        clip.DetachedFromVisualTree += (_, _) => timer.Stop();

        return clip;
    }
}
