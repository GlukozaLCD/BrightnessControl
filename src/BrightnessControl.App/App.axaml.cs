using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BrightnessControl.App.Services;
using BrightnessControl.App.Views;
using BrightnessControl.Core;

namespace BrightnessControl.App;

public partial class App : Application
{
    private BrightnessController? _brightnessController;
    private TrayService? _trayService;
    private TraySettingsStore? _traySettingsStore;
    private TraySettings? _traySettings;
    private AppSettingsStore? _appSettingsStore;
    private AppSettings? _appSettings;
    private BrightnessHudWindow? _hud;
    private SettingsWindow? _settingsWindow;
    private CoalescingBrightnessApplier? _globalApplier;
    private ScheduleEngine? _scheduleEngine;
    private AppProfileEngine? _appProfileEngine;
    private IdleEngine? _idleEngine;
    private AccentColorService? _accentColorService;
    private MonitorLockService? _monitorLockService;
    private int _globalPercent = 50;
    private int? _stickyClungValue;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Приложение живёт в трее — без главного окна на старте, процесс не
            // завершается при закрытии окон (закрытие происходит только через
            // явный Shutdown, например по клику "Выход" в меню трея).
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _brightnessController = new BrightnessController();
            _traySettingsStore = new TraySettingsStore();
            _traySettings = _traySettingsStore.Load();
            _appSettingsStore = new AppSettingsStore();
            _appSettings = _appSettingsStore.Load();
            ApplyTheme(_appSettings.Theme);
            _accentColorService = new AccentColorService(_appSettings, _appSettingsStore);
            _globalPercent = ComputeInitialGlobalPercent(_brightnessController);
            // FP14: "замочек" на яркость монитора — приоритет ВЫШЕ всех
            // существующих (простой/профиль/расписание) И выше глобального
            // слайдера "Все мониторы" (уточнено пользователем явно — лок должен
            // защищать и от собственной попытки пользователя сдвинуть ВСЕ
            // мониторы разом, не только от автоматики). Ручное управление
            // СОБСТВЕННЫМ слайдером залоченного монитора (в MonitorSlidersPopup)
            // лок не трогает — это единственный оставшийся путь менять его
            // яркость вручную.
            _monitorLockService = new MonitorLockService();
            _globalApplier = new CoalescingBrightnessApplier(
                percent =>
                {
                    if (_brightnessController is null)
                    {
                        return;
                    }

                    var targets = _brightnessController.Monitors
                        .Where(monitor => _monitorLockService?.IsLocked(monitor) != true)
                        .ToDictionary(monitor => monitor, _ => percent);
                    _brightnessController.SetEachBrightness(targets);
                },
                () => _brightnessController?.GetGlobalPacingMs() ?? 100);
            _hud = new BrightnessHudWindow(_traySettings);
            // Профиль приложения приоритетнее расписания: пока на мониторе активен
            // подходящий-под-профиль процесс, расписание этот монитор не трогает
            // (см. AppProfileEngine.ActiveMonitorAdapterDeviceName и FP5 Фазу 3).
            // Приглушение по бездействию (FP6) — глобальное и приоритетнее всех: пока
            // оно активно, расписание игнорирует ВСЕ мониторы (см. IdleEngine.IsDimmed).
            _appProfileEngine = new AppProfileEngine(
                _brightnessController,
                isMonitorLocked: monitor => _monitorLockService?.IsLocked(monitor) == true);
            _idleEngine = new IdleEngine(
                _brightnessController,
                resolveActiveProfilePercent: monitor => _appProfileEngine?.ActiveMonitorAdapterDeviceName == monitor.AdapterDeviceName
                    ? _appProfileEngine.ActiveProfilePercent
                    : null,
                isMonitorLocked: monitor => _monitorLockService?.IsLocked(monitor) == true);
            _scheduleEngine = new ScheduleEngine(
                _brightnessController,
                isMonitorSuppressed: monitor => _idleEngine?.IsDimmed == true
                    || _appProfileEngine?.ActiveMonitorAdapterDeviceName == monitor.AdapterDeviceName
                    || _monitorLockService?.IsLocked(monitor) == true);

            // При снятии лока монитор должен СРАЗУ получить то значение, которое
            // автоматика уже хочет прямо сейчас (приоритет простой → профиль →
            // расписание — тот же порядок, что и везде), а не ждать следующего
            // события/тика соответствующего движка.
            _monitorLockService.LockChanged += (monitor, locked) =>
            {
                if (locked || _brightnessController is null)
                {
                    return;
                }

                int? resyncValue = _idleEngine?.IsDimmed == true
                    ? null // простой сам восстановит при выходе — не вмешиваемся, пока активен
                    : _appProfileEngine?.ActiveMonitorAdapterDeviceName == monitor.AdapterDeviceName
                        ? _appProfileEngine.ActiveProfilePercent
                        : ComputeScheduleFallback(monitor);

                if (resyncValue is not null)
                {
                    _brightnessController.SetBrightness(monitor, resyncValue.Value);
                }
            };

            // FP11 — TrayIconDesignId может указывать либо на встроенную
            // векторную форму (enum), либо на свою импортированную растровую
            // иконку (см. TraySettings.CustomTrayIcons) — та же развилка, что и
            // в SettingsWindow.ApplyLiveIcon.
            var initialCustomIcon = _traySettings.CustomTrayIcons.FirstOrDefault(c => c.Id == _traySettings.TrayIconDesignId);
            var initialScale = _traySettings.GetTrayIconScale(_traySettings.TrayIconDesignId);

            System.Drawing.Icon initialTrayIcon;
            if (initialCustomIcon is not null)
            {
                initialTrayIcon = TrayIconRenderer.RenderCustom(CustomTrayIconStorage.GetFilePath(initialCustomIcon), initialScale);
            }
            else
            {
                if (!Enum.TryParse<TrayIconDesign>(_traySettings.TrayIconDesignId, out var initialDesign))
                {
                    initialDesign = TrayIconDesign.Spokes;
                }

                initialTrayIcon = TrayIconRenderer.Render(
                    initialDesign,
                    System.Drawing.ColorTranslator.FromHtml(_traySettings.TrayIconColorHex),
                    initialScale);
            }
            _trayService = new TrayService(initialTrayIcon, "BrightnessControl");
            _trayService.ScrollNotches += OnScrollNotches;
            _trayService.RightClicked += e => Dispatcher.UIThread.Post(() => OpenSettingsWindow(e.CursorX, e.CursorY, desktop));
            _trayService.LeftClicked += e => Dispatcher.UIThread.Post(() => OpenGlobalSliderPopup(e.CursorX, e.CursorY));
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(AppThemePreference preference)
    {
        if (Current is null)
        {
            return;
        }

        Current.RequestedThemeVariant = preference switch
        {
            AppThemePreference.Light => Avalonia.Styling.ThemeVariant.Light,
            AppThemePreference.Dark => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };
    }

    // FP14 — то же самое вычисление, что уже дважды продублировано в Core
    // (AppProfileEngine.ComputeScheduleFallback/IdleEngine.ComputeScheduleFallback) —
    // третья копия здесь осознанно, по тому же принципу: это маленький
    // самодостаточный расчёт "что расписание хочет для монитора ПРЯМО СЕЙЧАС",
    // не стоящий отдельной абстракции ради переиспользования между Core и App.
    private static int? ComputeScheduleFallback(MonitorInfo monitor)
    {
        var schedule = new JsonFileScheduleStore().Load();
        if (!schedule.IsEnabled || schedule.Rules.Count == 0)
        {
            return null;
        }

        var monitorKey = BrightnessController.GetMonitorKey(monitor);
        var applicableRules = schedule.Rules
            .Where(r => r.MonitorKeys.Count == 0 || r.MonitorKeys.Contains(monitorKey))
            .OrderBy(r => r.Time)
            .ToList();

        return applicableRules.Count == 0
            ? null
            : ScheduleEngine.FindActiveRule(applicableRules, TimeOnly.FromDateTime(DateTime.Now)).Percent;
    }

    private static int ComputeInitialGlobalPercent(BrightnessController controller)
    {
        var monitors = controller.Monitors;
        if (monitors.Count == 0)
        {
            return 50;
        }

        var sum = 0;
        var count = 0;
        foreach (var monitor in monitors)
        {
            var level = controller.GetBrightness(monitor);
            if (level is not null)
            {
                sum += level.Percent;
                count++;
            }
        }

        return count > 0 ? sum / count : 50;
    }

    private void OpenSettingsWindow(int cursorX, int cursorY, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_brightnessController is null)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(
                _brightnessController,
                _appSettings ?? new AppSettings(),
                _appSettingsStore ?? new AppSettingsStore(),
                _traySettings ?? new TraySettings(),
                _traySettingsStore ?? new TraySettingsStore(),
                _appProfileEngine,
                _idleEngine,
                _accentColorService,
                _trayService,
                () => desktop.Shutdown());
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        if (!(_trayService?.TryGetIconRect(out var iconRect) ?? false))
        {
            // Иконку не нашли (редкий случай) — прицепляемся к точке клика вместо неё.
            iconRect = new MonitorBounds(cursorX, cursorY, 0, 0);
        }

        _settingsWindow.ShowNearIcon(iconRect);
        _settingsWindow.Activate();
    }

    // Левый клик по иконке трея — лёгкий поповер только с яркостью (FP9 Фаза 2).
    // Правый клик по-прежнему открывает полное окно настроек (OpenSettingsWindow).
    private void OpenGlobalSliderPopup(int cursorX, int cursorY)
    {
        if (_brightnessController is null || _globalApplier is null)
        {
            return;
        }

        if (!(_trayService?.TryGetIconRect(out var iconRect) ?? false))
        {
            // Иконку не нашли (редкий случай) — прицепляемся к точке клика вместо неё.
            iconRect = new MonitorBounds(cursorX, cursorY, 0, 0);
        }

        var popup = new GlobalSliderPopup(_brightnessController, _globalApplier, _appSettings?.SliderStepPercent ?? 5, _monitorLockService!);
        popup.ShowNearIcon(iconRect);
    }

    private void OnScrollNotches(TrayScrollEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_traySettings?.IsScrollEnabled == false)
            {
                return;
            }

            var step = _traySettings?.ScrollStepPercent ?? 10;
            var stickyValues = _traySettings?.StickyValues ?? new List<int>();
            var direction = Math.Sign(e.Notches);

            for (var i = 0; i < Math.Abs(e.Notches); i++)
            {
                _globalPercent = ApplyStickyStep(_globalPercent, direction, step, stickyValues);
            }

            var iconRect = _trayService?.TryGetIconRect(out var rect) == true ? rect : new MonitorBounds(e.CursorX, e.CursorY, 0, 0);
            _hud?.ShowPercent(_globalPercent, iconRect);
            _globalApplier?.Request(_globalPercent);
        });
    }

    // "Липкие" значения: обычный плавный шаг по процентам, но если этот шаг
    // пересёк бы настроенное липкое значение — скролл вместо этого останавливается
    // точно на нём (на один тик), а следующий тик в ту же сторону просто продолжает
    // движение дальше. Любые непроходящие через липкие значения шаги не меняются.
    private int ApplyStickyStep(int current, int direction, int step, IReadOnlyList<int> stickyValues)
    {
        if (direction == 0)
        {
            return current;
        }

        if (_stickyClungValue == current && stickyValues.Contains(current))
        {
            _stickyClungValue = null;
            return Math.Clamp(current + direction * step, 0, 100);
        }

        var candidate = Math.Clamp(current + direction * step, 0, 100);
        var crossed = direction > 0
            ? stickyValues.Where(v => v > current && v <= candidate).OrderBy(v => v).Cast<int?>().FirstOrDefault()
            : stickyValues.Where(v => v < current && v >= candidate).OrderByDescending(v => v).Cast<int?>().FirstOrDefault();

        if (crossed is { } stickyValue)
        {
            _stickyClungValue = stickyValue;
            return stickyValue;
        }

        _stickyClungValue = null;
        return candidate;
    }
}
