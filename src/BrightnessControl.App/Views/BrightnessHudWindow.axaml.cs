using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

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

    // Показывает процент рядом с текущей позицией курсора и перезапускает таймер
    // автоскрытия — вызывается на каждый "тик" колеса, пока пользователь крутит его.
    public void ShowPercent(int percent, PixelPoint cursorPosition)
    {
        if (_percentText is not null)
        {
            _percentText.Text = $"{percent}%";
        }

        Position = new PixelPoint(cursorPosition.X + 16, cursorPosition.Y - (int)Height - 16);
        if (!IsVisible)
        {
            Show();
        }

        _hideTimer.Stop();
        _hideTimer.Start();
    }
}
