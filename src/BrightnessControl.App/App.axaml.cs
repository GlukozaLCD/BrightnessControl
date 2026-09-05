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
    private BrightnessHudWindow? _hud;
    private int _globalPercent = 50;

    private readonly object _brightnessWriteLock = new();
    private int? _pendingPercent;
    private bool _writeInProgress;

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
            _globalPercent = ComputeInitialGlobalPercent(_brightnessController);
            _hud = new BrightnessHudWindow();

            _trayService = new TrayService(
                new Uri("avares://BrightnessControl.App/Assets/avalonia-logo.ico"),
                "BrightnessControl");
            _trayService.ScrollNotches += OnScrollNotches;
            _trayService.BuildMenuItems = BuildTrayMenu;
            _trayService.MenuItemClicked += id => OnTrayMenuItemClicked(id, desktop);
        }

        base.OnFrameworkInitializationCompleted();
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

    // Windows нередко отдаёт одинаковое общее имя ("Generic PnP Monitor") для
    // нескольких разных мониторов — без номера адаптера их не отличить в меню.
    private static string FormatMenuLabel(MonitorInfo monitor)
    {
        var adapterShortName = monitor.AdapterDeviceName.TrimStart('\\', '.');
        return $"{monitor.FriendlyName} ({adapterShortName})";
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

            items.Add(new TrayMenuItem { Header = FormatMenuLabel(monitors[monitorIndex]), SubItems = presetItems });
        }

        items.Add(TrayMenuItem.Separator());
        items.Add(new TrayMenuItem { Header = "Открыть настройки", Id = OpenSettingsMenuId, IsEnabled = false });
        items.Add(TrayMenuItem.Separator());
        items.Add(new TrayMenuItem { Header = "Выход", Id = ExitMenuId });

        return items;
    }

    private void OnTrayMenuItemClicked(uint id, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (id == ExitMenuId)
        {
            Dispatcher.UIThread.Post(() => desktop.Shutdown());
            return;
        }

        if (id == OpenSettingsMenuId)
        {
            // Полноценное окно настроек появится в FP3 — сейчас пункт отключён (MF_GRAYED).
            return;
        }

        if (id >= PresetMenuIdBase)
        {
            var offset = id - PresetMenuIdBase;
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

    private void OnScrollNotches(TrayScrollEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var step = _traySettings?.ScrollStepPercent ?? 10;
            _globalPercent = Math.Clamp(_globalPercent + e.Notches * step, 0, 100);
            _hud?.ShowPercent(_globalPercent, new PixelPoint(e.CursorX, e.CursorY));
            RequestApplyBrightness(_globalPercent);
        });
    }

    // Запись яркости по DDC/CI может занимать до пары секунд (повторы для капризных
    // мониторов) — если делать это в UI-потоке на каждый "тик" колеса, приложение
    // ощутимо подвисает. Поэтому запись уходит в фон, а быстрые последовательные
    // скроллы схлопываются в одно применение последнего запрошенного значения, а не
    // выстраиваются в очередь одна за другой.
    private void RequestApplyBrightness(int percent)
    {
        lock (_brightnessWriteLock)
        {
            _pendingPercent = percent;
            if (_writeInProgress)
            {
                return;
            }

            _writeInProgress = true;
        }

        ThreadPool.QueueUserWorkItem(_ => ApplyPendingBrightnessLoop());
    }

    private void ApplyPendingBrightnessLoop()
    {
        while (true)
        {
            int percentToApply;
            lock (_brightnessWriteLock)
            {
                if (_pendingPercent is not { } pending)
                {
                    _writeInProgress = false;
                    return;
                }

                percentToApply = pending;
                _pendingPercent = null;
            }

            _brightnessController?.SetAllBrightness(percentToApply);
        }
    }
}
