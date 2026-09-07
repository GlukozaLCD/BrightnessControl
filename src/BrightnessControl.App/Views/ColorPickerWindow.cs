using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BrightnessControl.App.Views;

// Простое модальное окно выбора цвета (R/G/B-слайдеры + hex-поле) — свой цвет
// для палитры иконки трея (FP8). Без .axaml — весь UI маленький и собирается
// программно, как и остальные окна/вкладки в этом проекте.
//
// FP12 Фаза 4, п.7 — "Styled RGB" (единственный зафиксированный вариант из
// Фазы 3, без выбора между вариантами): та же самая функциональность
// R/G/B+hex, просто в токенах общего дизайна. Slider/Button уже стилизуются
// автоматически (глобальные ControlTheme в App.axaml применяются ко всем
// окнам приложения) — здесь донастраивается то, что глобальные темы не
// трогают: фон самого окна (без явного Background окно рисуется дефолтным
// светлым/системным, а не тёмной темой приложения), TextBox hex-поля
// (TextBox не входит в список стилизуемых стандартных контролов) и акцентная
// кнопка подтверждения.
public sealed class ColorPickerWindow : Window
{
    public string? ResultHex { get; private set; }

    public ColorPickerWindow(string initialHex)
    {
        Title = "Свой цвет";
        Width = 280;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        this.Bind(BackgroundProperty, this.GetResourceObservable("AppWindowBackground"));

        var initial = System.Drawing.ColorTranslator.FromHtml(initialHex);

        var preview = new Border
        {
            Height = 56,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(initial.R, initial.G, initial.B)),
        };
        preview.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppLineStrong"));

        var rSlider = BuildSlider(initial.R);
        var gSlider = BuildSlider(initial.G);
        var bSlider = BuildSlider(initial.B);
        var hexBox = BuildHexBox(initialHex);
        var suppressHexSync = false;

        void UpdateFromSliders()
        {
            var r = (byte)rSlider.Value;
            var g = (byte)gSlider.Value;
            var b = (byte)bSlider.Value;
            preview.Background = new SolidColorBrush(Color.FromRgb(r, g, b));

            suppressHexSync = true;
            hexBox.Text = $"#{r:X2}{g:X2}{b:X2}";
            suppressHexSync = false;
        }

        rSlider.ValueChanged += (_, _) => UpdateFromSliders();
        gSlider.ValueChanged += (_, _) => UpdateFromSliders();
        bSlider.ValueChanged += (_, _) => UpdateFromSliders();

        hexBox.TextChanged += (_, _) =>
        {
            if (suppressHexSync)
            {
                return;
            }

            try
            {
                var parsed = System.Drawing.ColorTranslator.FromHtml(hexBox.Text!.Trim());
                rSlider.Value = parsed.R;
                gSlider.Value = parsed.G;
                bSlider.Value = parsed.B;
                preview.Background = new SolidColorBrush(Color.FromRgb(parsed.R, parsed.G, parsed.B));
            }
            catch
            {
                // Некорректный hex во время набора — просто ждём, пока станет валидным.
            }
        };

        // Акцентная кнопка подтверждения (как "primary" в макете дизайн-токенов) —
        // локальные Background/Foreground/BorderThickness переопределяют
        // Setter'ы глобальной ControlTheme (обычные значения побеждают Style).
        var okButton = new Button { Content = "Добавить", BorderThickness = new Thickness(0) };
        okButton.Bind(Button.BackgroundProperty, this.GetResourceObservable("AppAccentBrush"));
        okButton.Bind(Button.ForegroundProperty, this.GetResourceObservable("AppAccentOnBrush"));
        okButton.Click += (_, _) =>
        {
            ResultHex = hexBox.Text;
            Close();
        };

        var cancelButton = new Button { Content = "Отмена" };
        cancelButton.Click += (_, _) => Close();

        var buttonsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttonsRow.Children.Add(cancelButton);
        buttonsRow.Children.Add(okButton);

        var root = new StackPanel { Margin = new Thickness(18), Spacing = 12 };
        root.Children.Add(preview);
        root.Children.Add(BuildLabeledRow("R", rSlider));
        root.Children.Add(BuildLabeledRow("G", gSlider));
        root.Children.Add(BuildLabeledRow("B", bSlider));
        root.Children.Add(BuildLabeledRow("Hex", hexBox));
        root.Children.Add(buttonsRow);

        Content = root;
    }

    private static Slider BuildSlider(byte initial) => new() { Minimum = 0, Maximum = 255, Value = initial, Width = 170 };

    private TextBox BuildHexBox(string initialHex)
    {
        var box = new TextBox
        {
            Text = initialHex,
            Width = 110,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        box.Bind(TextBox.BackgroundProperty, this.GetResourceObservable("AppSurfaceSunken"));
        box.Bind(TextBox.ForegroundProperty, this.GetResourceObservable("AppInk"));
        box.Bind(TextBox.BorderBrushProperty, this.GetResourceObservable("AppLineStrong"));
        return box;
    }

    private Control BuildLabeledRow(string label, Control control)
    {
        var text = new TextBlock
        {
            Text = label,
            Width = 30,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        };
        text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppMuted"));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(text);
        row.Children.Add(control);
        return row;
    }
}
