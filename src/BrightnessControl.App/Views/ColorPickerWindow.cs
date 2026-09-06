using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BrightnessControl.App.Views;

// Простое модальное окно выбора цвета (R/G/B-слайдеры + hex-поле) — свой цвет
// для палитры иконки трея (FP8). Без .axaml — весь UI маленький и собирается
// программно, как и остальные окна/вкладки в этом проекте.
public sealed class ColorPickerWindow : Window
{
    public string? ResultHex { get; private set; }

    public ColorPickerWindow(string initialHex)
    {
        Title = "Свой цвет";
        Width = 260;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var initial = System.Drawing.ColorTranslator.FromHtml(initialHex);

        var preview = new Border
        {
            Height = 44,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(initial.R, initial.G, initial.B)),
        };

        var rSlider = BuildSlider(initial.R);
        var gSlider = BuildSlider(initial.G);
        var bSlider = BuildSlider(initial.B);
        var hexBox = new TextBox { Text = initialHex, Width = 110 };
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

        var okButton = new Button { Content = "Добавить" };
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

        var root = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        root.Children.Add(preview);
        root.Children.Add(BuildLabeledRow("R", rSlider));
        root.Children.Add(BuildLabeledRow("G", gSlider));
        root.Children.Add(BuildLabeledRow("B", bSlider));
        root.Children.Add(BuildLabeledRow("Hex", hexBox));
        root.Children.Add(buttonsRow);

        Content = root;
    }

    private static Slider BuildSlider(byte initial) => new() { Minimum = 0, Maximum = 255, Value = initial, Width = 170 };

    private static Control BuildLabeledRow(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = label, Width = 30, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }
}
