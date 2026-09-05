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
    private const uint ExitMenuId = 1;
    private const uint OpenSettingsMenuId = 2;
    private const uint PresetMenuIdBase = 1000;
    private static readonly int[] PresetPercents = { 25, 50, 75, 100 };

    private BrightnessController? _brightnessController;
    private TrayService? _trayService;
    private TraySettingsStore? _traySettingsStore;
    private TraySettings? _traySettings;
    private AppSettingsStore? _appSettingsStore;
    private BrightnessHudWindow? _hud;
    private SettingsWindow? _settingsWindow;
    private CoalescingBrightnessApplier? _globalApplier;
    private int _globalPercent = 50;

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
            ApplyTheme(_appSettingsStore.Load().Theme);
            _globalPercent = ComputeInitialGlobalPercent(_brightnessController);
            _globalApplier = new CoalescingBrightnessApplier(
                percent => _brightnessController?.SetAllBrightness(percent),
                () => _brightnessController?.GetGlobalPacingMs() ?? 100);
            _hud = new BrightnessHudWindow();

            _trayService = new TrayService(
                new Uri("avares://BrightnessControl.App/Assets/avalonia-logo.ico"),
                "BrightnessControl");
            _trayService.ScrollNotches += OnScrollNotches;
            _trayService.BuildMenuItems = BuildTrayMenu;
            _trayService.MenuItemClicked += e => OnTrayMenuItemClicked(e, desktop);
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

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        var items = new List<TrayMenuItem>();
        var monitors = _brightnessController?.Monitors ?? Array.Empty<MonitorInfo>();

        for (var monitorIndex = 0; monitorIndex < monitors.Count; monitorIndex++)
        {
            var presetItems = new List<TrayMenuItem>();
            for (var presetIndex = 0; presetIndex < PresetPercents.Length; presetIndex++)
            {
                var id = PresetMenuIdBase + (uint)monitorIndex * 10 + (uint)presetIndex;
                presetItems.Add(new TrayMenuItem { Header = $"{PresetPercents[presetIndex]}%", Id = id });
            }

            items.Add(new TrayMenuItem { Header = MonitorLabel.Format(monitors[monitorIndex]), SubItems = presetItems });
        }

        items.Add(TrayMenuItem.Separator());
        items.Add(new TrayMenuItem { Header = "Открыть настройки", Id = OpenSettingsMenuId });
        items.Add(TrayMenuItem.Separator());
        items.Add(new TrayMenuItem { Header = "Выход", Id = ExitMenuId });

        return items;
    }

    private void OnTrayMenuItemClicked(TrayMenuClickEventArgs e, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (e.Id == ExitMenuId)
        {
            Dispatcher.UIThread.Post(() => desktop.Shutdown());
            return;
        }

        if (e.Id == OpenSettingsMenuId)
        {
            Dispatcher.UIThread.Post(() => OpenSettingsWindow(e.CursorX, e.CursorY));
            return;
        }

        if (e.Id >= PresetMenuIdBase)
        {
            var offset = e.Id - PresetMenuIdBase;
            var monitorIndex = (int)(offset / 10);
            var presetIndex = (int)(offset % 10);
            var monitors = _brightnessController?.Monitors;

            if (monitors is not null && monitorIndex < monitors.Count && presetIndex < PresetPercents.Length)
            {
                var monitor = monitors[monitorIndex];
                var percent = PresetPercents[presetIndex];
                ThreadPool.QueueUserWorkItem(_ => _brightnessController?.SetBrightness(monitor, percent));
            }
        }
    }

    private void OpenSettingsWindow(int cursorX, int cursorY)
    {
        if (_brightnessController is null)
        {
            return;
        }

        var targetMonitor = MonitorLookup.FindAtPoint(_brightnessController.Monitors, cursorX, cursorY)
            ?? _brightnessController.Monitors.FirstOrDefault();

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_brightnessController, _appSettingsStore ?? new AppSettingsStore());
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
            var step = _traySettings?.ScrollStepPercent ?? 10;
            _globalPercent = Math.Clamp(_globalPercent + e.Notches * step, 0, 100);
            _hud?.ShowPercent(_globalPercent, new PixelPoint(e.CursorX, e.CursorY));
            _globalApplier?.Request(_globalPercent);
        });
    }
}
