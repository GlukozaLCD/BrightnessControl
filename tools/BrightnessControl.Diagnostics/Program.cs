using BrightnessControl.Core;
using System.Windows.Forms;

if (args.Contains("overlay-demo"))
{
    RunOverlayDemo();
    return;
}

if (args.Contains("set-all") && args.Length > 1)
{
    RunSetAll(int.Parse(args[1]));
    return;
}

if (args.Contains("watch-foreground"))
{
    RunWatchForeground();
    return;
}

var monitors = MonitorEnumerator.EnumerateMonitors();
var ddcCi = new DdcCiBrightnessProvider();
var wmi = new WmiBrightnessProvider();

Console.WriteLine($"Найдено мониторов: {monitors.Count}");
Console.WriteLine();

foreach (var monitor in monitors)
{
    Console.WriteLine($"Адаптер:      {monitor.AdapterDeviceName}");
    Console.WriteLine($"Имя:          {monitor.FriendlyName}");
    Console.WriteLine($"DeviceId:     {monitor.DeviceId}");
    Console.WriteLine($"Тип:          {monitor.ConnectionKind}");

    if (monitor.ConnectionKind == MonitorConnectionKind.ExternalDdcCi)
    {
        var level = ddcCi.GetBrightness(monitor);
        Console.WriteLine(level is null
            ? "Яркость:      не удалось прочитать (DDC/CI)"
            : $"Яркость:      {level.Percent}% (raw {level.Current} из [{level.Minimum};{level.Maximum}], DDC/CI)");
    }
    else if (monitor.ConnectionKind == MonitorConnectionKind.InternalPanel)
    {
        var level = wmi.GetBrightness(monitor);
        Console.WriteLine(level is null
            ? "Яркость:      не удалось прочитать (WMI)"
            : $"Яркость:      {level.Percent}% (WMI)");
    }

    Console.WriteLine(new string('-', 60));
}

static void RunSetAll(int percent)
{
    using var controller = new BrightnessController();

    Console.WriteLine("До изменения:");
    foreach (var monitor in controller.Monitors)
    {
        Console.WriteLine($"  {monitor.FriendlyName} [{monitor.ConnectionKind}]: {controller.GetBrightness(monitor)?.Percent.ToString() ?? "null"}%");
    }

    Console.WriteLine($"Ставлю {percent}% сразу на все мониторы...");
    controller.SetAllBrightness(percent);

    Console.WriteLine("После изменения:");
    foreach (var monitor in controller.Monitors)
    {
        Console.WriteLine($"  {monitor.FriendlyName} [{monitor.ConnectionKind}]: {controller.GetBrightness(monitor)?.Percent.ToString() ?? "null"}%");
    }
}

static void RunWatchForeground()
{
    Console.WriteLine("Слежу за передним окном 30 секунд — переключайтесь между окнами/мониторами...");

    using var watcher = new ForegroundAppWatcher();
    watcher.ForegroundChanged += info =>
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] процесс={info.ProcessName}, заголовок=\"{info.WindowTitle}\", монитор={info.MonitorAdapterDeviceName}");
    };
    watcher.ReportCurrentForegroundWindow();

    Thread.Sleep(30000);
    Console.WriteLine("Готово.");
}

static void RunOverlayDemo()
{
    var fakeMonitors = MonitorEnumerator.EnumerateMonitors()
        .Select(m => m with { ConnectionKind = MonitorConnectionKind.Unsupported })
        .ToList();

    using var provider = new OverlayFallbackBrightnessProvider();

    using var form = new Form
    {
        Text = "BrightnessControl — тест оверлея",
        Width = 420,
        Height = 170,
        StartPosition = FormStartPosition.CenterScreen,
        TopMost = true,
        FormBorderStyle = FormBorderStyle.FixedDialog,
        MaximizeBox = false,
        MinimizeBox = false,
    };

    var label = new Label
    {
        Text = "Затемнение: 0%",
        Dock = DockStyle.Top,
        Height = 30,
        TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
    };

    var slider = new TrackBar
    {
        Minimum = 0,
        Maximum = 100,
        Value = 0,
        TickFrequency = 10,
        Dock = DockStyle.Top,
    };

    var closeButton = new Button
    {
        Text = "Сбросить и закрыть",
        Dock = DockStyle.Bottom,
        Height = 40,
    };

    void ApplyDim(int dimPercent)
    {
        label.Text = $"Затемнение: {dimPercent}%";
        var brightnessPercent = 100 - dimPercent;
        foreach (var monitor in fakeMonitors)
        {
            provider.SetBrightness(monitor, brightnessPercent);
        }
    }

    slider.Scroll += (_, _) => ApplyDim(slider.Value);
    closeButton.Click += (_, _) =>
    {
        slider.Value = 0;
        ApplyDim(0);
        form.Close();
    };

    form.Controls.Add(closeButton);
    form.Controls.Add(slider);
    form.Controls.Add(label);

    Console.WriteLine($"Оверлей-демо: {fakeMonitors.Count} монитор(ов). Двигайте ползунок в открывшемся окне.");
    Application.EnableVisualStyles();
    Application.Run(form);
    Console.WriteLine("Окно закрыто, оверлеи сброшены.");
}
