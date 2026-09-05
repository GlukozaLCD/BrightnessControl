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
            _globalPercent = ComputeInitialGlobalPercent(_brightnessController);
            _globalApplier = new CoalescingBrightnessApplier(
                percent => _brightnessController?.SetAllBrightness(percent),
                () => _brightnessController?.GetGlobalPacingMs() ?? 100);
            _hud = new BrightnessHudWindow();
            _scheduleEngine = new ScheduleEngine(_brightnessController);

            _trayService = new TrayService(
                new Uri("avares://BrightnessControl.App/Assets/avalonia-logo.ico"),
                "BrightnessControl");
            _trayService.ScrollNotches += OnScrollNotches;
            _trayService.RightClicked += e => Dispatcher.UIThread.Post(() => OpenSettingsWindow(e.CursorX, e.CursorY, desktop));
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

        var targetMonitor = MonitorLookup.FindAtPoint(_brightnessController.Monitors, cursorX, cursorY)
            ?? _brightnessController.Monitors.FirstOrDefault();

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(
                _brightnessController,
                _appSettings ?? new AppSettings(),
                _appSettingsStore ?? new AppSettingsStore(),
                _traySettings ?? new TraySettings(),
                _traySettingsStore ?? new TraySettingsStore(),
                () => desktop.Shutdown());
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        if (targetMonitor is not null)
        {
            _settingsWindow.ShowCenteredOn(targetMonitor.Bounds);
        }
        else
        {
            _settingsWindow.Show();
        }

        _settingsWindow.Activate();
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

            _hud?.ShowPercent(_globalPercent, new PixelPoint(e.CursorX, e.CursorY));
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
