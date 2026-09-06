using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BrightnessControl.Core;

namespace BrightnessControl.App.Views;

public partial class BrightnessHudWindow : Window
{
    private readonly DispatcherTimer _hideTimer;
    private TextBlock? _percentText;

    public BrightnessHudWindow()
    {
        InitializeComponent();
        _percentText = this.FindControl<TextBlock>("PercentText");
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Показывает процент и перезапускает таймер автоскрытия — вызывается на
    // каждый "тик" колеса, пока пользователь крутит его над иконкой трея.
    // Позиция — та же общая логика, что и у SettingsWindow/GlobalSliderPopup
    // (см. TrayPopupPlacement): прижато к краю монитора/панели задач, а не
    // просто "рядом с курсором" — раньше HUD мог оказаться где угодно у самого
    // края экрана вместе с курсором.
    public void ShowPercent(int percent, MonitorBounds iconRect)
    {
        if (_percentText is not null)
        {
            _percentText.Text = $"{percent}%";
        }

        if (!IsVisible)
        {
            Show();
        }

        Position = TrayPopupPlacement.Compute(Screens, iconRect.X, iconRect.Y, iconRect.Width, iconRect.Height, (int)Width, (int)Height);

        _hideTimer.Stop();
        _hideTimer.Start();
    }
}
