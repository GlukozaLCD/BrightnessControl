using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
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
    private readonly AccentColorService? _accentColorService;
    private readonly TrayService? _trayService;
    private readonly Action _onExitRequested;

    // FP13 — единственный экземпляр окна предпросмотра темы: повторный клик
    // на кнопку должен активировать уже открытое окно, а не плодить дубликаты.
    private ThemePreviewWindow? _themePreviewWindow;

    // Двухуровневая навигация (FP9 Фаза 3): _selectedCategory == null — показан
    // список категорий верхнего уровня; иначе — подкатегории ВЫБРАННОЙ категории
    // (список целиком подменяется, а не разворачивается на месте — решено заранее).
    private List<NavCategory> _rootCategories = new();
    private NavCategory? _selectedCategory;
    private NavSubcategory? _selectedSubcategory;
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
        _accentColorService = null;
        _trayService = null;
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
        AccentColorService? accentColorService,
        TrayService? trayService,
        Action onExitRequested)
    {
        _controller = controller;
        _appSettings = appSettings;
        _appSettingsStore = appSettingsStore;
        _traySettings = traySettings;
        _traySettingsStore = traySettingsStore;
        _appProfileEngine = appProfileEngine;
        _idleEngine = idleEngine;
        _accentColorService = accentColorService;
        _trayService = trayService;
        _onExitRequested = onExitRequested;
        InitializeComponent();
        BuildContent();
        SetupCloseButton();

        // Ведёт себя как всплывающее меню трея: закрывается, стоит только кликнуть
        // мимо — а не как обычное окно настроек, которое остаётся открытым.
        // _suppressDeactivateClose снимает это на время показа дочернего диалога
        // (см. ColorPickerWindow) — иначе открытие диалога само по себе забирает
        // фокус ОС у этого окна, оно считается "деактивированным" и тут же
        // закрывается, из-за чего казалось, что всё приложение исчезает.
        //
        // _themePreviewWindow (FP13) — та же проблема, но окно НЕМОДАЛЬНОЕ и
        // должно жить долго (пока пользователь крутит настройки рядом), а не
        // на краткий момент одного диалога, поэтому проверяется отдельно, а не
        // через _suppressDeactivateClose (тот включается/выключается только
        // вокруг Show/ShowDialog конкретного вызова).
        Deactivated += (_, _) =>
        {
            if (!_suppressDeactivateClose && _themePreviewWindow is null)
            {
                Close();
            }
        };
    }

    private bool _suppressDeactivateClose;

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

    // Прицеплено к иконке трея (как GlobalSliderPopup), а не по центру монитора
    // клика — FP9 Фаза 6: центрирование по монитору для окна настроек "всё ещё
    // не устраивало" пользователя после того, как левый клик забрал себе поповер.
    // Сама сторона/сторона панели — общая логика, см. TrayPopupPlacement (её же
    // использует GlobalSliderPopup и BrightnessHudWindow).
    public void ShowNearIcon(MonitorBounds iconRect)
    {
        if (!IsVisible)
        {
            Show();
        }

        Position = TrayPopupPlacement.Compute(Screens, iconRect.X, iconRect.Y, iconRect.Width, iconRect.Height, (int)Width, (int)Height);
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
        SetupContentPanelBlur();
        SelectSubcategory(_rootCategories[0].Subcategories[0]);
    }

    // Клик по совсем пустому месту вкладки (не по конкретному контролу) сам по
    // себе никуда фокус не переводит — Avalonia не "уводит в никуда" фокус с
    // текстового поля просто потому, что кликнули мимо. e.Source сравнивается
    // именно с самим ContentPanel — событие доходит и от кликов по дочерним
    // Border/TextBlock (у них своих обработчиков нет), но у них e.Source будет
    // ЭТОТ дочерний элемент, а не панель, так что реальные ряды настроек клик
    // не перехватывают.
    private void SetupContentPanelBlur()
    {
        var contentPanel = this.FindControl<StackPanel>("ContentPanel")!;
        contentPanel.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, contentPanel))
            {
                contentPanel.Focus();
            }
        };
    }

    // Кнопка сверху списка перекидывает сам список категорий между правым и левым
    // краем окна. Раньше была стрелкой ("←"/"→") — но выглядела так же, как кнопка
    // "Назад" в подкатегориях, путала. Теперь — маленькая иконка-диаграмма макета
    // (два блока: узкая полоса-панель + широкая область), показывающая ТЕКУЩУЮ
    // сторону списка, а не направление клика — см. BuildLayoutFlipIcon.
    private void SetupNavFlip()
    {
        var grid = this.FindControl<Grid>("NavContentGrid")!;
        var navBorder = this.FindControl<Border>("NavBorder")!;
        var contentScroll = (Control)grid.Children.First(c => c is ScrollViewer);
        var flipButton = this.FindControl<Button>("NavFlipButton")!;

        flipButton.Content = BuildLayoutFlipIcon(_navOnRight);
        ToolTip.SetTip(flipButton, "Переместить список категорий на другую сторону окна");

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
            flipButton.Content = BuildLayoutFlipIcon(_navOnRight);
        };
    }

    // Маленькая диаграмма окна: внешняя рамка + внутренняя перегородка, узкая
    // закрашенная полоса — там, где СЕЙЧАС находится список категорий (не куда он
    // поедет по клику, а где он есть прямо сейчас) — так пользователь всегда видит
    // текущий макет, а не гадает по стрелке.
    private static Control BuildLayoutFlipIcon(bool navOnRight)
    {
        var outline = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x90, 0x90, 0x90));

        var grid = new Grid
        {
            ColumnDefinitions = navOnRight ? new ColumnDefinitions("*,Auto") : new ColumnDefinitions("Auto,*"),
        };
        var panel = new Border { Width = 5, Background = outline };
        Grid.SetColumn(panel, navOnRight ? 1 : 0);
        grid.Children.Add(panel);

        return new Border
        {
            Width = 18,
            Height = 14,
            CornerRadius = new CornerRadius(2),
            BorderBrush = outline,
            BorderThickness = new Thickness(1.3),
            Child = grid,
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
                navList.Children.Add(BuildNavRow(category.Title, isActive: false, () =>
                {
                    _selectedCategory = category;
                    SelectSubcategory(category.Subcategories[0]);
                }));
            }

            return;
        }

        navList.Children.Add(BuildNavRow("← Назад", isActive: false, () =>
        {
            _selectedCategory = null;
            RenderNavList();
        }));
        navList.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 4) });

        foreach (var subcategory in _selectedCategory.Subcategories)
        {
            var isActive = ReferenceEquals(subcategory, _selectedSubcategory);
            navList.Children.Add(BuildNavRow(subcategory.Title, isActive, () => SelectSubcategory(subcategory)));
        }
    }

    // Пункт списка навигации — Border+TextBlock вместо Button: список категорий
    // должен читаться именно как СПИСОК с одним акцентно закрашенным на всю
    // строку активным пунктом (см. референс Volumey settings panel), а не как
    // набор одинаковых кнопок без разницы между активным/неактивным состоянием
    // (FP12, "непонятно куда ты заходишь"). Цвета — через GetResourceObservable,
    // а не разовый снимок ресурса, чтобы подсветка не "залипала" на старом
    // акцентном цвете при live-смене акцента Windows.
    private Border BuildNavRow(string title, bool isActive, Action onClick)
    {
        var text = new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = isActive ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal,
        };
        text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(isActive ? "AppAccentOnBrush" : "AppInk"));

        var row = new Border
        {
            Child = text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        if (isActive)
        {
            row.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppAccentBrush"));
        }
        else
        {
            row.Background = Avalonia.Media.Brushes.Transparent;
            // FP13: приглушённый акцентный тинт вместо нейтрального
            // AppSurfaceHover — та же роль (AppAccentBackgroundBrush), что и
            // у фона карточки Compact Bar (BrightnessHudWindow), для единого
            // ощущения "это подсвечено акцентом", а не просто "это серее".
            //
            // ВАЖНО: Bind() создаёт ЖИВУЮ подписку на ресурс — раньше (когда
            // цвет наведения был статичным AppSurfaceHover) её не отключали,
            // это было безобидно. Теперь AppAccentBackgroundBrush меняется
            // при переключении настроек акцента (SwapAccentRoles, чекбокс
            // Windows-акцента и т.п.) — если не отключить старую подписку
            // явно, она продолжает жить и переписывает Background обратно на
            // акцентный тон при следующей смене ресурса, ДАЖЕ ЕСЛИ курсор уже
            // давно ушёл с этого пункта (сложный баг, найденный пользователем:
            // "навигация по категориям, потом переключение акцента").
            IDisposable? hoverBinding = null;
            row.PointerEntered += (_, _) => hoverBinding = row.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppAccentBackgroundBrush"));
            row.PointerExited += (_, _) =>
            {
                hoverBinding?.Dispose();
                hoverBinding = null;
                row.Background = Avalonia.Media.Brushes.Transparent;
            };
        }

        row.PointerPressed += (_, _) => onClick();

        return row;
    }

    // Единица измерения встроена как InnerRightContent, а НЕ через литерал в
    // FormatString ("0 'мин'") — тот подход смешивал единицу с редактируемым
    // текстом самого поля: пользователь мог случайно стереть/повредить "мин"
    // при ручном вводе, и это же ломало commit по Enter/клику мимо поля
    // (парсинг спотыкался о оставшиеся обрывки суффикса). InnerRightContent —
    // отдельный визуальный элемент внутри рамки, не участвующий в
    // редактируемом Text/Value вообще, поэтому не мешает ни вводу, ни commit.
    private NumericUpDown BuildNumericStepper(decimal minimum, decimal maximum, decimal value, string unit)
    {
        var suffix = new TextBlock
        {
            Text = unit,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        suffix.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppMuted"));

        var stepper = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Width = 150,
            FormatString = "0",
            InnerRightContent = suffix,
        };

        // Встроенный коммит текста у NumericUpDown ненадёжен (ни Enter, ни клик
        // мимо поля не применяли набранное значение на практике) — коммитим
        // вручную: парсим Text и выставляем Value сами. Невалидный текст (или
        // пустое поле) откатывается обратно к текущему Value, а не оставляет
        // "битую" строку в поле.
        void CommitText()
        {
            if (decimal.TryParse(stepper.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out var parsed))
            {
                stepper.Value = Math.Clamp(parsed, stepper.Minimum, stepper.Maximum);
            }
            else
            {
                stepper.Text = stepper.Value?.ToString("0", System.Globalization.CultureInfo.CurrentCulture);
            }
        }

        stepper.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitText();
                // Window как цель фокуса не подходит (фокус реально не уходит
                // с текстового поля — курсор-каретка и выделение остаются на
                // месте). ContentPanel сделан Focusable в XAML специально под
                // это — реальная фокусируемая, но не текстовая цель.
                this.FindControl<StackPanel>("ContentPanel")?.Focus();
                e.Handled = true;
            }
        };
        stepper.LostFocus += (_, _) => CommitText();

        return stepper;
    }

    private void SelectSubcategory(NavSubcategory subcategory)
    {
        _selectedSubcategory = subcategory;
        RenderNavList();

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
        sliderStepRow.Children.Add(new TextBlock { Text = "Шаг слайдера:", VerticalAlignment = VerticalAlignment.Center, Width = 150 });
        var sliderStepUpDown = BuildNumericStepper(1, 50, _appSettings.SliderStepPercent, "%");
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
        stepRow.Children.Add(new TextBlock { Text = "Шаг скролла:", VerticalAlignment = VerticalAlignment.Center, Width = 150 });
        var stepUpDown = BuildNumericStepper(1, 50, _traySettings.ScrollStepPercent, "%");
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

        var addLabel = new TextBlock { Text = "Новое значение:", VerticalAlignment = VerticalAlignment.Center };
        var addValueInput = BuildNumericStepper(0, 100, 50, "%");
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

    // FP12 Фаза 4, п.8 — "+"/"×" рисуются векторной геометрией (Line), а не
    // TextBlock: TextBlock центрирует текст по LINE BOX шрифта (полная высота
    // ascent+descent), а не по фактическим закрашенным пикселям конкретного
    // глифа — у символов "+"/"×" реальная "чернильная" область заметно уже и
    // расположена не строго по центру line box, из-за чего центрирование по
    // умолчанию давало видимое смещение влево-вниз. Линии центрируются по
    // РЕАЛЬНОЙ геометрии фигуры, поэтому не "плавают" в зависимости от шрифта.
    private static Control BuildPlusGlyph(double size, double thickness, Avalonia.Media.IBrush stroke)
    {
        var half = size / 2;
        var canvas = new Canvas { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(half, 0),
            EndPoint = new Point(half, size),
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeLineCap = Avalonia.Media.PenLineCap.Round,
        });
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(0, half),
            EndPoint = new Point(size, half),
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeLineCap = Avalonia.Media.PenLineCap.Round,
        });
        return canvas;
    }

    private static Control BuildCrossGlyph(double size, double thickness, Avalonia.Media.IBrush stroke)
    {
        var canvas = new Canvas { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(size, size),
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeLineCap = Avalonia.Media.PenLineCap.Round,
        });
        canvas.Children.Add(new Line
        {
            StartPoint = new Point(size, 0),
            EndPoint = new Point(0, size),
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeLineCap = Avalonia.Media.PenLineCap.Round,
        });
        return canvas;
    }

    // FP13 — свой акцентный цвет, виден только пока чекбокс Windows-акцента
    // снят (иначе базовый цвет и так берётся из системы, выбирать нечего).
    // Переиспользует тот же ColorPickerWindow, что и свой цвет иконки трея
    // (FP8) — только по подтверждению ("Изменить" → диалог → OK), не вживую
    // по ходу перетаскивания слайдеров внутри пикера.
    private Control BuildCustomAccentSection()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };

        var initialColor = Avalonia.Media.Color.TryParse(_appSettings.CustomAccentColorHex, out var parsedInitial)
            ? parsedInitial
            : Avalonia.Media.Color.FromRgb(0x00, 0x78, 0xD4);

        var swatch = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Background = new Avalonia.Media.SolidColorBrush(initialColor),
        };
        swatch.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppLineStrong"));

        var changeButton = new Button { Content = "Изменить" };
        changeButton.Click += async (_, _) =>
        {
            _suppressDeactivateClose = true;
            try
            {
                var picker = new ColorPickerWindow(_appSettings.CustomAccentColorHex);
                await picker.ShowDialog(this);

                if (picker.ResultHex is { } hex)
                {
                    swatch.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex));
                    _accentColorService?.SetCustomAccentColor(hex);
                }
            }
            finally
            {
                _suppressDeactivateClose = false;
            }
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new TextBlock { Text = "Свой акцентный цвет:", VerticalAlignment = VerticalAlignment.Center, Width = 180 });
        row.Children.Add(swatch);
        row.Children.Add(changeButton);
        panel.Children.Add(row);

        return panel;
    }

    private ComboBox BuildColorHarmonySelector()
    {
        var options = new (ColorHarmonyScheme Value, string Label)[]
        {
            (ColorHarmonyScheme.Monochromatic, "Монохромная"),
            (ColorHarmonyScheme.AnalogousClose, "Соседняя, узкая"),
            (ColorHarmonyScheme.Analogous, "Соседняя"),
            (ColorHarmonyScheme.AnalogousWide, "Соседняя, широкая"),
        };

        var current = Enum.TryParse<ColorHarmonyScheme>(_appSettings.ColorHarmonySchemeId, out var parsedScheme)
            ? parsedScheme
            : ColorHarmonyScheme.Analogous;

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

            _accentColorService?.SetColorHarmonyScheme(options[comboBox.SelectedIndex].Value);
        };

        return comboBox;
    }

    // FP13 — открывает окно предпросмотра темы НЕМОДАЛЬНО (Show, не
    // ShowDialog), рядом с SettingsWindow: пользователь должен иметь
    // возможность крутить слайдеры/комбобоксы настроек и сразу видеть эффект
    // в предпросмотре, не закрывая ни то, ни другое окно. Повторный клик на
    // кнопку активирует уже открытое окно вместо создания дубликата.
    private void OpenThemePreview()
    {
        if (_themePreviewWindow is not null)
        {
            _themePreviewWindow.Activate();
            return;
        }

        _themePreviewWindow = new ThemePreviewWindow();
        _themePreviewWindow.Closed += (_, _) => _themePreviewWindow = null;
        _themePreviewWindow.Show(this);
    }

    private void BuildAppearanceTab(StackPanel root)
    {
        root.Children.Add(new TextBlock { Text = "Тема", FontWeight = Avalonia.Media.FontWeight.Bold });
        root.Children.Add(BuildThemeSelector());

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        var accentCheckBox = new CheckBox { Content = "Использовать акцентный цвет Windows", IsChecked = _appSettings.UseWindowsAccentColor };
        ToolTip.SetTip(accentCheckBox, "Подкрашивает выделение/акцентные элементы в цвет, который вы выбрали в Параметры Windows → Персонализация → Цвета, вместо стандартного синего.");
        root.Children.Add(accentCheckBox);

        // Комбинация — ВСЕГДА видна (действует независимо от того, откуда взят
        // основной цвет, из Windows или свой), а свой базовый цвет — только
        // пока Windows-акцент выключен (FP13).
        var colorHarmonyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        colorHarmonyRow.Children.Add(new TextBlock { Text = "Цветовая комбинация:", VerticalAlignment = VerticalAlignment.Center, Width = 180 });
        colorHarmonyRow.Children.Add(BuildColorHarmonySelector());
        ToolTip.SetTip(colorHarmonyRow, "Дополнительные акцентные элементы (например, вторые лучи HUD-солнца) получают цвет, вычисленный из основного акцента по этому правилу.");
        root.Children.Add(colorHarmonyRow);

        var customAccentSection = BuildCustomAccentSection();
        customAccentSection.IsVisible = !(accentCheckBox.IsChecked ?? true);
        root.Children.Add(customAccentSection);

        accentCheckBox.IsCheckedChanged += (_, _) =>
        {
            var enabled = accentCheckBox.IsChecked ?? true;
            _accentColorService?.SetEnabled(enabled);
            customAccentSection.IsVisible = !enabled;
        };

        var swapRolesCheckBox = new CheckBox { Content = "Поменять акцент и противоположный цвет местами", IsChecked = _appSettings.SwapAccentRoles };
        ToolTip.SetTip(swapRolesCheckBox, "Тот же набор вычисленных цветов — меняется только, какой из них применяется как основной акцент по всему приложению, а какой как противоположный.");
        swapRolesCheckBox.IsCheckedChanged += (_, _) => _accentColorService?.SetSwapAccentRoles(swapRolesCheckBox.IsChecked ?? false);
        root.Children.Add(swapRolesCheckBox);

        var previewButton = new Button { Content = "Открыть предпросмотр темы", Margin = new Thickness(0, 4, 0, 0) };
        ToolTip.SetTip(previewButton, "Отдельное немодальное окошко со сводкой элементов интерфейса — удобно держать открытым рядом с настройками для быстрой оценки сочетания цветов.");
        previewButton.Click += (_, _) => OpenThemePreview();
        root.Children.Add(previewButton);

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        root.Children.Add(new TextBlock { Text = "HUD с процентом", FontWeight = Avalonia.Media.FontWeight.Bold });
        ToolTip.SetTip(root.Children[^1], "Всплывающее окошко с процентом, которое появляется при скролле над иконкой трея.");
        root.Children.Add(BuildHudStyleSelector());

        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        root.Children.Add(new TextBlock { Text = "Иконка трея", FontWeight = Avalonia.Media.FontWeight.Bold });
        root.Children.Add(BuildTrayIconSection());
    }

    // Стиль HUD — параметрический выбор (FP12 Фаза 4, п.6), по аналогии с
    // формой иконки трея (FP8): пользователь выбирает готовый стиль вместо
    // подстройки параметров вручную. Сам HUD-window уже создан и живёт всё
    // время работы приложения (см. App.axaml.cs) — она читает
    // TraySettings.HudStyleId заново при каждом показе, отдельно уведомлять
    // её о смене настройки не нужно.
    private ComboBox BuildHudStyleSelector()
    {
        var options = new (HudStyle Value, string Label)[]
        {
            (HudStyle.GrowingRaysSun, "Растущее солнце"),
            (HudStyle.CompactBar, "Компактная шкала"),
            (HudStyle.PillToast, "Капсула"),
        };

        var currentStyle = Enum.TryParse<HudStyle>(_traySettings.HudStyleId, out var parsed) ? parsed : HudStyle.GrowingRaysSun;

        var comboBox = new ComboBox
        {
            ItemsSource = options.Select(o => o.Label).ToList(),
            SelectedIndex = Array.FindIndex(options, o => o.Value == currentStyle),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 200,
        };

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedIndex < 0)
            {
                return;
            }

            _traySettings.HudStyleId = options[comboBox.SelectedIndex].Value.ToString();
            _traySettingsStore.Save(_traySettings);
        };

        return comboBox;
    }

    // Форма, цвет и масштаб иконки трея — три независимых параметра (FP8):
    // иконка рисуется на лету (TrayIconRenderer), а не грузится из готового
    // файла, поэтому любую комбинацию можно применить сразу — без пересборки
    // и без необходимости хранить файл на каждую комбинацию. Масштаб — свой
    // на каждую форму (крутится колесом мыши над карточкой), не общий слайдер.
    private Control BuildTrayIconSection()
    {
        var panel = new StackPanel { Spacing = 6 };
        var mutedBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0xA0, 0x80, 0x80, 0x80));
        var accentBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF2, 0x90, 0x0C));
        var neutralBorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x40, 0x80, 0x80, 0x80));
        var handCursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);

        // SettingsWindow фиксированного размера (Width=760, CanResize="False" в
        // .axaml — увеличено с исходных 640 именно чтобы в галерею форм влезало
        // 4 карточки в ряд, не только 3) — доступную ширину под галерею можно
        // посчитать заранее по известным размерам разметки, не дожидаясь
        // реального прохода layout (в момент построения контента окно ещё не
        // показано, Bounds всех элементов ещё нулевые). Если разметка
        // окна/навигации изменится, эти числа нужно будет поправить вручную:
        // 760 (окно) − 2 (внешний Border BorderThickness=1×2) − 170 (NavBorder) −
        // 32 (ContentPanel Margin=16×2) − 18 (запас на вертикальный скроллбар,
        // если содержимое вкладки не помещается по высоте).
        const int availableGalleryWidth = 760 - 2 - 170 - 32 - 18;
        const int cardTotalWidth = 104 + 8; // сама карточка (см. ниже) + Margin(4) с каждой стороны
        var maxDesignColumns = Math.Max(1, availableGalleryWidth / cardTotalWidth);
        var designColumns = ComputeOptimalColumns(TrayIconCatalog.Designs.Count, maxDesignColumns);

        var designGallery = new UniformGrid { Columns = designColumns };
        panel.Children.Add(new TextBlock { Text = "Форма", FontSize = 12, Foreground = mutedBrush });
        panel.Children.Add(designGallery);
        panel.Children.Add(new TextBlock
        {
            Text = "Прокрутите колесо мыши над формой, чтобы изменить её масштаб — у каждой формы он свой.",
            FontSize = 10,
            Foreground = mutedBrush,
            FontStyle = Avalonia.Media.FontStyle.Italic,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var colorRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "Цвет", FontSize = 12, Foreground = mutedBrush, Margin = new Thickness(0, 6, 0, 0) });
        panel.Children.Add(colorRow);

        string? renamingDesignId = null;
        var isCommittingRename = false;

        void ApplyLiveIcon()
        {
            if (!Enum.TryParse<TrayIconDesign>(_traySettings.TrayIconDesignId, out var design))
            {
                design = TrayIconDesign.Spokes;
            }

            var scale = _traySettings.GetTrayIconScale(_traySettings.TrayIconDesignId);
            var color = System.Drawing.ColorTranslator.FromHtml(_traySettings.TrayIconColorHex);
            var icon = TrayIconRenderer.Render(design, color, scale);
            _trayService?.SetIcon(icon);
            _traySettingsStore.Save(_traySettings);
        }

        // Формы, ещё не встречавшиеся в TrayIconDesignOrder (новые, добавленные уже
        // после того как порядок сохранился), уходят в конец в порядке каталога —
        // так список остаётся стабильным при добавлении новых форм и не требует
        // отдельной миграции сохранённых настроек.
        List<TrayIconDesignOption> GetOrderedDesigns()
        {
            var byId = TrayIconCatalog.Designs.ToDictionary(o => o.Design.ToString());
            var ordered = new List<TrayIconDesignOption>();

            foreach (var id in _traySettings.TrayIconDesignOrder)
            {
                if (byId.Remove(id, out var option))
                {
                    ordered.Add(option);
                }
            }

            foreach (var option in TrayIconCatalog.Designs)
            {
                if (byId.ContainsKey(option.Design.ToString()))
                {
                    ordered.Add(option);
                }
            }

            return ordered;
        }

        void MoveDesign(string designId, int direction)
        {
            var orderedIds = GetOrderedDesigns().Select(o => o.Design.ToString()).ToList();
            var index = orderedIds.IndexOf(designId);
            var newIndex = index + direction;
            if (newIndex < 0 || newIndex >= orderedIds.Count)
            {
                return;
            }

            (orderedIds[index], orderedIds[newIndex]) = (orderedIds[newIndex], orderedIds[index]);
            _traySettings.TrayIconDesignOrder = orderedIds;
            _traySettingsStore.Save(_traySettings);
            RefreshDesignGallery();
        }

        void RefreshDesignGallery()
        {
            designGallery.Children.Clear();
            var color = System.Drawing.ColorTranslator.FromHtml(_traySettings.TrayIconColorHex);
            var orderedDesigns = GetOrderedDesigns();

            for (var designIndex = 0; designIndex < orderedDesigns.Count; designIndex++)
            {
                var option = orderedDesigns[designIndex];
                var designId = option.Design.ToString();
                var isSelected = designId == _traySettings.TrayIconDesignId;
                var scale = _traySettings.GetTrayIconScale(designId);
                var displayName = _traySettings.TrayIconDesignNameOverrides.TryGetValue(designId, out var custom)
                    ? custom
                    : option.DisplayName;

                var preview = new Image
                {
                    Width = 32,
                    Height = 32,
                    Source = TrayIconRenderer.RenderPreview(option.Design, color, scale),
                };

                Control nameControl;
                if (renamingDesignId == designId)
                {
                    var nameBox = new TextBox { Text = displayName, Width = 68, FontSize = 10 };
                    // Клик внутри поля ввода не должен всплыть до карточки и переключить
                    // выбор формы посреди редактирования имени.
                    nameBox.PointerPressed += (_, e) => e.Handled = true;
                    nameBox.KeyDown += (_, e) =>
                    {
                        if (e.Key == Key.Enter)
                        {
                            CommitRename(designId, nameBox.Text);
                        }
                    };
                    nameBox.LostFocus += (_, _) => CommitRename(designId, nameBox.Text);
                    nameControl = nameBox;
                    Dispatcher.UIThread.Post(() => nameBox.Focus(), DispatcherPriority.Background);
                }
                else
                {
                    var nameText = new TextBlock
                    {
                        Text = displayName,
                        FontSize = 10,
                        MaxWidth = 56,
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                    };
                    ToolTip.SetTip(nameText, displayName);

                    var pencil = new TextBlock { Text = "✎", FontSize = 9, Foreground = mutedBrush, Cursor = handCursor };
                    ToolTip.SetTip(pencil, "Переименовать");
                    pencil.PointerPressed += (_, e) =>
                    {
                        e.Handled = true;
                        renamingDesignId = designId;
                        RefreshDesignGallery();
                    };

                    var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
                    nameRow.Children.Add(nameText);
                    nameRow.Children.Add(pencil);
                    nameControl = nameRow;
                }

                var scaleText = new TextBlock { Text = $"{scale}%", FontSize = 9, Foreground = mutedBrush };

                // Сортировка — кнопки "влево/вправо" вместо drag-and-drop: проще и
                // надёжнее, тот же стиль, что и остальные явные кнопки в проекте
                // (± у процента, крестик удаления цвета). Порядок — это индекс в
                // GetOrderedDesigns(), а не визуальная позиция в WrapPanel (та может
                // переноситься на новую строку независимо от логического порядка).
                //
                // Кнопки встроены в САМУ карточку по бокам (не отдельным рядом снизу):
                // узкие полосы во всю высоту, каждая скруглена только со своей стороны
                // (как угол карточки) — так они читаются как часть силуэта плитки, а
                // не как отдельные наклеенные поверх кружки.
                var canMoveLeft = designIndex > 0;
                var canMoveRight = designIndex < orderedDesigns.Count - 1;
                var arrowActiveBg = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x1C, 0x80, 0x80, 0x80));
                var arrowInactiveBg = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x08, 0x80, 0x80, 0x80));

                var leftArrow = new Border
                {
                    Width = 16,
                    CornerRadius = new CornerRadius(7, 0, 0, 7),
                    Background = canMoveLeft ? arrowActiveBg : arrowInactiveBg,
                    Cursor = canMoveLeft ? handCursor : null,
                    Child = new TextBlock
                    {
                        Text = "◀",
                        FontSize = 9,
                        Foreground = canMoveLeft ? mutedBrush : neutralBorderBrush,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                var rightArrow = new Border
                {
                    Width = 16,
                    CornerRadius = new CornerRadius(0, 7, 7, 0),
                    Background = canMoveRight ? arrowActiveBg : arrowInactiveBg,
                    Cursor = canMoveRight ? handCursor : null,
                    Child = new TextBlock
                    {
                        Text = "▶",
                        FontSize = 9,
                        Foreground = canMoveRight ? mutedBrush : neutralBorderBrush,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                ToolTip.SetTip(leftArrow, "Сдвинуть влево");
                ToolTip.SetTip(rightArrow, "Сдвинуть вправо");
                leftArrow.PointerPressed += (_, e) => { e.Handled = true; MoveDesign(designId, -1); };
                rightArrow.PointerPressed += (_, e) => { e.Handled = true; MoveDesign(designId, 1); };

                var centerContent = new StackPanel
                {
                    Spacing = 4,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { preview, nameControl, scaleText },
                };

                var cardLayout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                Grid.SetColumn(leftArrow, 0);
                Grid.SetColumn(centerContent, 1);
                Grid.SetColumn(rightArrow, 2);
                cardLayout.Children.Add(leftArrow);
                cardLayout.Children.Add(centerContent);
                cardLayout.Children.Add(rightArrow);

                var card = new Border
                {
                    Width = 104,
                    Height = 88,
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(8),
                    // Прозрачный, но НЕ null — Border без явного фона не ловит клики в
                    // "пустых" местах (за пределами children), только на самих детях.
                    Background = Avalonia.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(isSelected ? 2 : 1),
                    BorderBrush = isSelected ? accentBrush : neutralBorderBrush,
                    // Иначе прямоугольные боковые полосы (leftArrow/rightArrow) торчали
                    // бы за пределы скруглённых внешних углов карточки — Avalonia Border
                    // по умолчанию не обрезает содержимое по своей геометрии.
                    ClipToBounds = true,
                    Cursor = handCursor,
                    Child = cardLayout,
                };

                card.PointerPressed += (_, _) =>
                {
                    _traySettings.TrayIconDesignId = designId;
                    ApplyLiveIcon();
                    RefreshDesignGallery();
                };

                // Масштаб — индивидуальный на каждую форму: скролл над карточкой, а не
                // общий контрол на всю галерею.
                card.PointerWheelChanged += (_, e) =>
                {
                    e.Handled = true;
                    var current = _traySettings.GetTrayIconScale(designId);
                    var next = Math.Clamp(current + (e.Delta.Y > 0 ? 5 : -5), 100, 170);
                    _traySettings.TrayIconScaleByDesign[designId] = next;

                    if (isSelected)
                    {
                        ApplyLiveIcon();
                    }
                    else
                    {
                        _traySettingsStore.Save(_traySettings);
                    }

                    RefreshDesignGallery();
                };

                designGallery.Children.Add(card);
            }
        }

        void CommitRename(string designId, string? newName)
        {
            // Children.Clear() внутри RefreshDesignGallery() ниже синхронно отбирает
            // фокус у ещё "живого" TextBox, из-за чего LostFocus срабатывает ПОВТОРНО
            // прямо посреди этого же вызова (реентерабельно) — без этой защиты каждое
            // переименование через Enter+клик-мимо запускало вложенный Clear()/Add(),
            // из-за чего часть карточек добавлялась в галерею дважды.
            if (isCommittingRename)
            {
                return;
            }

            isCommittingRename = true;
            try
            {
                newName = newName?.Trim();
                if (!string.IsNullOrEmpty(newName))
                {
                    _traySettings.TrayIconDesignNameOverrides[designId] = newName;
                    _traySettingsStore.Save(_traySettings);
                }

                renamingDesignId = null;
                RefreshDesignGallery();
            }
            finally
            {
                isCommittingRename = false;
            }
        }

        void RefreshColorRow()
        {
            colorRow.Children.Clear();

            foreach (var hex in ColorSort.SortByHue(_traySettings.TrayIconColors))
            {
                var isSelected = string.Equals(hex, _traySettings.TrayIconColorHex, StringComparison.OrdinalIgnoreCase);

                var swatch = new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(14),
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex)),
                    BorderThickness = new Thickness(isSelected ? 3 : 1),
                    BorderBrush = isSelected
                        ? accentBrush
                        : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x60, 0x80, 0x80, 0x80)),
                    Cursor = handCursor,
                };
                ToolTip.SetTip(swatch, hex);

                swatch.PointerPressed += (_, _) =>
                {
                    _traySettings.TrayIconColorHex = hex;
                    ApplyLiveIcon();
                    RefreshColorRow();
                    RefreshDesignGallery();
                };

                // Тот же визуальный язык, что уже использован для "скрыть процесс" в
                // профилях приложений — маленький серый кружок с крестиком в углу.
                var removeGlyph = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(7),
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x90, 0x90, 0x90)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, -3, -3, 0),
                    Cursor = handCursor,
                    Child = BuildCrossGlyph(7, 1.4, Avalonia.Media.Brushes.White),
                };
                removeGlyph.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    _traySettings.TrayIconColors.Remove(hex);

                    if (string.Equals(_traySettings.TrayIconColorHex, hex, StringComparison.OrdinalIgnoreCase)
                        && _traySettings.TrayIconColors.Count > 0)
                    {
                        _traySettings.TrayIconColorHex = _traySettings.TrayIconColors[0];
                        ApplyLiveIcon();
                        RefreshDesignGallery();
                    }
                    else
                    {
                        _traySettingsStore.Save(_traySettings);
                    }

                    RefreshColorRow();
                };

                var cell = new Grid { Margin = new Thickness(0, 0, 4, 4) };
                cell.Children.Add(swatch);
                cell.Children.Add(removeGlyph);
                colorRow.Children.Add(cell);
            }

            var addButton = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                // Прозрачный, но НЕ null — тот же баг, что уже чинили для карточек
                // форм: Border без явного фона не ловит клики в "пустых" местах,
                // только на самих детях, так что клик срабатывал лишь если попасть
                // точно в тонкий символ "+", а не по всему кругу.
                Background = Avalonia.Media.Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = neutralBorderBrush,
                Cursor = handCursor,
                Margin = new Thickness(0, 0, 4, 4),
                Child = BuildPlusGlyph(14, 2, mutedBrush),
            };
            ToolTip.SetTip(addButton, "Добавить свой цвет");
            addButton.PointerPressed += async (_, _) =>
            {
                _suppressDeactivateClose = true;
                try
                {
                    var picker = new ColorPickerWindow(_traySettings.TrayIconColorHex);
                    await picker.ShowDialog(this);

                    if (picker.ResultHex is { } hex && !_traySettings.TrayIconColors.Contains(hex, StringComparer.OrdinalIgnoreCase))
                    {
                        _traySettings.TrayIconColors.Add(hex);
                        _traySettingsStore.Save(_traySettings);
                        RefreshColorRow();
                    }
                }
                finally
                {
                    _suppressDeactivateClose = false;
                }
            };
            colorRow.Children.Add(addButton);
        }

        RefreshDesignGallery();
        RefreshColorRow();

        return panel;
    }

    private void BuildScheduleTab(StackPanel root)
    {
        var scheduleStore = new JsonFileScheduleStore();
        var scheduleSettings = scheduleStore.Load();
        var monitorNames = new MonitorNameStore().Load();

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
                    Text = $"{MonitorLabel.Format(monitor, monitorNames)}: {active.Time:HH:mm} → {active.Percent}%",
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
        var rulesHeaderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("60,75,*,Auto") };
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
                            ? MonitorLabel.Format(found, monitorNames)
                            : key));

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("60,75,*,Auto") };
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
        percentRow.Children.Add(new TextBlock { Text = "Яркость:", VerticalAlignment = VerticalAlignment.Center, Width = 150 });
        var percentInput = BuildNumericStepper(0, 100, 80, "%");
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
            var checkBox = new CheckBox { Content = MonitorLabel.Format(monitor, monitorNames), IsChecked = true, IsEnabled = false };
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
        var monitorNames = new MonitorNameStore().Load();

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
            var monitorLabel = monitor is not null ? MonitorLabel.Format(monitor, monitorNames) : activeMonitorAdapterName ?? "?";

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
        percentRow.Children.Add(new TextBlock { Text = "Яркость:", VerticalAlignment = VerticalAlignment.Center, Width = 150 });
        var percentInput = BuildNumericStepper(0, 100, 50, "%");
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
        timeoutRow.Children.Add(new TextBlock { Text = "Таймаут простоя:", VerticalAlignment = VerticalAlignment.Center, Width = 180 });
        var timeoutInput = BuildNumericStepper(1, 180, idleSettings.IdleTimeoutMinutes, "мин");
        timeoutRow.Children.Add(timeoutInput);
        root.Children.Add(timeoutRow);

        var dimPercentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        dimPercentRow.Children.Add(new TextBlock { Text = "Яркость при простое:", VerticalAlignment = VerticalAlignment.Center, Width = 180 });
        var dimPercentInput = BuildNumericStepper(0, 100, idleSettings.DimPercent, "%");
        dimPercentRow.Children.Add(dimPercentInput);
        root.Children.Add(dimPercentRow);

        var pollIntervalRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var pollIntervalLabel = new TextBlock { Text = "Проверка простоя:", VerticalAlignment = VerticalAlignment.Center, Width = 180 };
        ToolTip.SetTip(pollIntervalLabel, "Как часто проверяется, не пошевелили ли вы мышью/клавиатурой. Меньше — " +
            "отзывчивее восстановление после простоя, но чуть чаще фоновая проверка.");
        pollIntervalRow.Children.Add(pollIntervalLabel);
        var pollIntervalInput = BuildNumericStepper(1, 60, idleSettings.PollIntervalSeconds, "сек");
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
    internal static Slider AddSliderRow(StackPanel root, string label, int initialPercent, int tickStep, Action<int> onChanged, double nameColumnWidth = 360, bool allowForceResync = false)
        => AddSliderRow(root, BuildMarqueeLabel(label, nameColumnWidth), initialPercent, tickStep, onChanged, allowForceResync);

    // Перегрузка, принимающая уже готовый control вместо голой строки — нужна
    // MonitorSlidersPopup (FP8/переименование мониторов), где название должно быть
    // кликабельным (переключается в поле ввода) и нести маленькую иконку пера, а
    // не просто быть бегущей строкой без взаимодействия.
    internal static Slider AddSliderRow(StackPanel root, Control nameLabel, int initialPercent, int tickStep, Action<int> onChanged, bool allowForceResync = false)
    {
        var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        // Ширина ФИКСИРОВАНА (не по содержимому) — иначе "Auto"-колонка меняла размер
        // на каждый тик процента (9% уже, 100% шире), сосед в "*"-колонке от этого
        // ужимался/расширялся и дёргался при каждом изменении яркости.
        var percentLabel = new TextBlock { Text = $"{initialPercent}%", Width = 42, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(nameLabel, 0);
        Grid.SetColumn(percentLabel, 1);
        headerRow.Children.Add(nameLabel);
        headerRow.Children.Add(percentLabel);

        // FP15 — клик по проценту принудительно ПЕРЕОТПРАВЛЯЕТ ТЕКУЩЕЕ
        // значение на все мониторы, без изменения самого числа: тот же
        // эффект, что раньше пользователь получал вручную через "−1", потом
        // "+1" (значение визуально не меняется, но в железо уходит новая
        // команда) — нужно, если какой-то монитор физически "разъехался" со
        // значением, которое помнит слайдер (например, яркость подкрутили
        // прямо на самом мониторе кнопками). Это НЕ поле ввода — просто
        // повторный вызов onChanged с уже текущим значением. Пользователь
        // явно попросил ТОЛЬКО для глобального слайдера ("Все мониторы" в
        // GlobalSliderPopup), не для слайдеров по отдельным мониторам —
        // отсюда параметр allowForceResync, а не безусловно для всех
        // вызовов AddSliderRow.
        if (allowForceResync)
        {
            percentLabel.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            ToolTip.SetTip(percentLabel, "Нажмите, чтобы заново применить это значение ко всем мониторам");
        }

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

        if (allowForceResync)
        {
            percentLabel.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                e.Handled = true;
                onChanged((int)slider.Value);
            }, handledEventsToo: true);
        }

        // FP15 — колесо мыши над строкой слайдера меняет значение, по
        // аналогии со скроллом над иконкой трея (FP2). Шаг — tickStep (тот
        // же параметр, что уже используется для прилипания при
        // перетаскивании, отдельной настройки не заводили). Shift — точная
        // подстройка ±1% в обход шага (та же идея, что и в
        // BuildScrollableTwoDigit для формы расписания, хотя там у Shift
        // обратный смысл — здесь именно так решил пользователь). Пишем в
        // slider.Value, а не напрямую вызываем onChanged — тогда срабатывает
        // тот же PropertyChanged-обработчик выше (раз isDragging=false,
        // onChanged вызовется сразу на каждый тик), без дублирования кода
        // применения; коалесцирование в железо — забота вызывающей стороны
        // (GlobalSliderPopup/MonitorSlidersPopup уже оборачивают onChanged в
        // CoalescingBrightnessApplier, как и скролл над иконкой трея).
        //
        // Обработчик висит НЕ на самом слайдере, а на широкой обёртке ВСЕЙ
        // строки (подпись+процент сверху, минус/слайдер/плюс снизу) — сам
        // визуальный трек слайдера слишком тонкий, пользователю было трудно
        // "попасть" в него курсором именно для скролла (найдено по живому
        // фидбеку: "мышка не считается над активной областью").
        void HandleWheel(object? sender, PointerWheelEventArgs e)
        {
            e.Handled = true;
            var notches = Math.Sign(e.Delta.Y);
            if (notches == 0)
            {
                return;
            }

            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : tickStep;
            slider.Value = Math.Clamp(slider.Value + notches * step, slider.Minimum, slider.Maximum);
        }

        // Padding=0 — общий Button ControlTheme (FP12) задаёт Padding="14,8" для
        // обычных текстовых кнопок ("Сохранить" и т.п.); при ширине всего 32px
        // это почти не оставляет места самому символу "−"/"+", и он выглядит
        // как еле заметная точка.
        var minusButton = new Button
        {
            Content = "−",
            Width = 32,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var plusButton = new Button
        {
            Content = "+",
            Width = 32,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
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

        var rowContainer = new StackPanel();
        rowContainer.Children.Add(headerRow);
        rowContainer.Children.Add(sliderHost);
        // StackPanel без явного Background не участвует в хит-тесте за
        // пределами своих детей (та же история, что уже чинили для кнопки
        // "+" — Border/Panel без Background не ловит клики/скролл в
        // "пустых" промежутках между children), поэтому без этого колесо
        // между headerRow и sliderHost попросту не долетало бы до обработчика.
        rowContainer.Background = Avalonia.Media.Brushes.Transparent;
        rowContainer.AddHandler(InputElement.PointerWheelChangedEvent, HandleWheel, handledEventsToo: true);

        root.Children.Add(rowContainer);
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

    // Раньше это был RenderTransform (сдвиг X) + Border с ClipToBounds — на практике
    // ломало раскладку всей строки (текст "убегал" на соседнюю строку окна ниже),
    // видимо из-за того, как Avalonia сочетает трансформацию рендера с обрезкой у
    // родителя. Полностью отказались от transform/clip: вместо визуального сдвига
    // готового TextBlock просто подменяем САМ ТЕКСТ на видимое окно символов (как
    // старая бегущая строка на LCD-табло) — это не может сломать раскладку, потому
    // что каждый кадр — это просто обычная строка, умещающаяся в отведённую ширину.
    // Internal — переиспользуется MonitorSlidersPopup (FP8/переименование мониторов)
    // для построения кликабельного названия монитора, не только этим окном.
    // Число колонок для галереи форм иконки трея (см. BuildTrayIconSection) — не
    // просто "сколько влезает по ширине", а лучшее среди [maxColumns-1, maxColumns]
    // по заполненности последней строки. Без этого ограничения снизу (только -1,
    // не перебор всех вариантов до 1) идеальным "нулевым остатком" всегда выглядит
    // 1 колонка (последняя "строка" из одного элемента тривиально заполнена целиком) —
    // формально верно, но превращает галерею в бесполезный вертикальный список.
    // Например, 6 форм при maxColumns=4 лягут в 3 колонки (3+3), а не в 4 (4+2).
    private static int ComputeOptimalColumns(int totalCount, int maxColumns)
    {
        if (totalCount <= 0)
        {
            return Math.Max(1, maxColumns);
        }

        var minColumns = Math.Max(1, maxColumns - 1);
        var best = maxColumns;
        var bestPadding = int.MaxValue;

        for (var cols = maxColumns; cols >= minColumns; cols--)
        {
            var rows = (int)Math.Ceiling(totalCount / (double)cols);
            var padding = cols * rows - totalCount;

            if (padding < bestPadding || (padding == bestPadding && cols > best))
            {
                bestPadding = padding;
                best = cols;
            }
        }

        return best;
    }

    internal static Control BuildMarqueeLabel(string text, double width)
    {
        var textBlock = new TextBlock
        {
            Width = width,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
        };

        // Сколько символов от offset реально помещается в width — раньше это была
        // грубая оценка "7px на символ", независимая от реального шрифта. Проблема:
        // заглавные буквы и цифры ("...DISPLAY3)") заметно шире этой средней
        // оценки, поэтому реально отрисованная строка оказывалась ШИРЕ отведённого
        // места, и Avalonia молча обрезала лишний хвост — обычно как раз последний
        // символ (закрывающую скобку), хотя сама логика прокрутки считала его
        // показанным целиком.
        //
        // Первая попытка честного измерения мерила текст через САМ textBlock — но у
        // него уже задано фиксированное Width, а явно заданное Width у Avalonia
        // ограничивает результат Measure() сверху: DesiredSize.Width никогда не
        // превышал width, даже когда реальный текст был шире, — из-за этого
        // "проверка" всегда считала, что текст помещается целиком, и анимация вообще
        // переставала запускаться (текст просто показывался статично обрезанным).
        // Измеряем поэтому ОТДЕЛЬНЫМ TextBlock без заданной ширины — со скопированным
        // шрифтом реального лейбла (стиль темы к моменту AttachedToVisualTree уже
        // точно применён), но без ограничения, которое мешало бы Measure() увидеть
        // реальный размер контента.
        var measurer = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.NoWrap };

        int CountFittingChars(int offset)
        {
            var maxChars = text.Length - offset;
            for (var count = maxChars; count > 1; count--)
            {
                measurer.Text = text.Substring(offset, count);
                measurer.Measure(Size.Infinity);
                if (measurer.DesiredSize.Width <= width)
                {
                    return count;
                }
            }

            return Math.Min(1, maxChars);
        }

        DispatcherTimer? timer = null;
        var initialized = false;

        textBlock.AttachedToVisualTree += (_, _) =>
        {
            if (initialized)
            {
                return;
            }

            initialized = true;

            measurer.FontFamily = textBlock.FontFamily;
            measurer.FontSize = textBlock.FontSize;
            measurer.FontWeight = textBlock.FontWeight;
            measurer.FontStyle = textBlock.FontStyle;

            if (CountFittingChars(0) >= text.Length)
            {
                textBlock.Text = text;
                return;
            }

            var offset = 0;
            var forward = true;
            var pauseTicksRemaining = 0;
            const int pauseTicksAtEnds = 8; // ~1.6с на паузу, чтобы конец/начало успевали прочитаться

            // Максимальный сдвиг вправо — минимальный offset, при котором ОСТАТОК
            // строки уже помещается в width целиком (дальше двигать некуда, конец
            // текста и так весь виден).
            var maxOffset = 0;
            while (maxOffset < text.Length && CountFittingChars(maxOffset) < text.Length - maxOffset)
            {
                maxOffset++;
            }

            void Refresh() => textBlock.Text = text.Substring(offset, CountFittingChars(offset));
            Refresh();

            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (_, _) =>
            {
                if (pauseTicksRemaining > 0)
                {
                    pauseTicksRemaining--;
                    return;
                }

                offset += forward ? 1 : -1;
                if (offset >= maxOffset)
                {
                    offset = maxOffset;
                    forward = false;
                    pauseTicksRemaining = pauseTicksAtEnds;
                }
                else if (offset <= 0)
                {
                    offset = 0;
                    forward = true;
                    pauseTicksRemaining = pauseTicksAtEnds;
                }

                Refresh();
            };
            timer.Start();
        };

        // Иначе таймер продолжит тикать вечно в фоне после закрытия окна —
        // строка больше не в дереве, значения меняются, но их никто не видит.
        textBlock.DetachedFromVisualTree += (_, _) => timer?.Stop();

        return textBlock;
    }
}
