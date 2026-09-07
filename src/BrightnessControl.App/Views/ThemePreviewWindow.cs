using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BrightnessControl.App.Views;

// FP13 — окно живого предпросмотра темы: открывается НЕ модально рядом с
// SettingsWindow, чтобы можно было менять акцент/схему/тему в настройках и
// сразу видеть эффект здесь, не переключаясь между окнами. Живое обновление
// не требует отдельного кода — все брэши здесь подписаны через
// GetResourceObservable/DynamicResource на те же ресурсы (AppAccentBrush и
// т.п.), что и весь остальной UI, а стандартные контролы (Button/CheckBox/
// Slider/ComboBox/NumericUpDown) сами подхватывают глобальные ControlTheme
// из App.axaml — здесь ничего специально не стилизуется заново.
public sealed class ThemePreviewWindow : Window
{
    public ThemePreviewWindow()
    {
        Title = "Предпросмотр темы";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Topmost = true;

        this.Bind(BackgroundProperty, this.GetResourceObservable("AppWindowBackground"));

        var root = new StackPanel { Margin = new Thickness(18), Spacing = 16 };

        root.Children.Add(BuildAccentSwatchesSection());
        root.Children.Add(new Separator());
        root.Children.Add(BuildNavSampleSection());
        root.Children.Add(new Separator());
        root.Children.Add(BuildControlsSection());

        Content = new ScrollViewer { Content = root, MaxHeight = 640 };
    }

    private Control BuildSectionHeader(string text)
    {
        var header = new TextBlock { Text = text, FontWeight = FontWeight.SemiBold };
        header.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppMuted"));
        return header;
    }

    // Три пары "тон + его вариант" (уточнено пользователем, 2026-09-07) —
    // сгруппированы визуально по рядам, чтобы пара читалась как пара.
    private Control BuildAccentSwatchesSection()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(BuildSectionHeader("Акцент"));

        var accentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        accentRow.Children.Add(BuildSwatch("AppAccentBrush", "Акцент"));
        accentRow.Children.Add(BuildSwatch("AppAccentVariantBrush", "Акцент, тон 2"));
        panel.Children.Add(accentRow);

        var oppositeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        oppositeRow.Children.Add(BuildSwatch("AppAccentOppositeBrush", "Противоположный"));
        oppositeRow.Children.Add(BuildSwatch("AppAccentOppositeVariantBrush", "Противоп., тон 2"));
        panel.Children.Add(oppositeRow);

        var backgroundRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        backgroundRow.Children.Add(BuildSwatch("AppAccentBackgroundBrush", "Фон под акцент"));
        backgroundRow.Children.Add(BuildSwatch("AppAccentBackgroundVariantBrush", "Фон, тон 2"));
        panel.Children.Add(backgroundRow);

        return panel;
    }

    private Control BuildSwatch(string resourceKey, string label)
    {
        var swatch = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
        };
        swatch.Bind(Border.BackgroundProperty, this.GetResourceObservable(resourceKey));
        swatch.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppLineStrong"));

        var text = new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11 };
        text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppMuted"));

        var column = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        column.Children.Add(swatch);
        column.Children.Add(text);
        return column;
    }

    // Тот же визуальный язык, что и активный/неактивный пункт бокового списка
    // категорий в SettingsWindow (см. BuildNavRow, FP12) — своя копия здесь
    // намеренно: окно предпросмотра не должно зависеть от private-методов
    // SettingsWindow.
    private Control BuildNavSampleSection()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(BuildSectionHeader("Список"));
        panel.Children.Add(BuildNavRowSample("Активный пункт", isActive: true));
        panel.Children.Add(BuildNavRowSample("Обычный пункт", isActive: false));
        return panel;
    }

    private Control BuildNavRowSample(string label, bool isActive)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(isActive ? "AppAccentOnBrush" : "AppInk"));

        var row = new Border
        {
            Child = text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        row.Bind(Border.BackgroundProperty, this.GetResourceObservable(isActive ? "AppAccentBrush" : "AppSurface"));
        return row;
    }

    private Control BuildControlsSection()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(BuildSectionHeader("Контролы"));

        panel.Children.Add(new CheckBox { Content = "Тумблер", IsChecked = true });

        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 65 };
        panel.Children.Add(slider);

        var comboBox = new ComboBox { ItemsSource = new[] { "Пример 1", "Пример 2" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(comboBox);

        var stepper = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 100,
            Value = 42,
            FormatString = "0",
            InnerRightContent = BuildUnitLabel("%"),
        };
        panel.Children.Add(stepper);

        var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttonsRow.Children.Add(new Button { Content = "Кнопка" });

        var accentButton = new Button { Content = "Основное действие", BorderThickness = new Thickness(0) };
        accentButton.Bind(Button.BackgroundProperty, this.GetResourceObservable("AppAccentBrush"));
        accentButton.Bind(Button.ForegroundProperty, this.GetResourceObservable("AppAccentOnBrush"));
        buttonsRow.Children.Add(accentButton);

        panel.Children.Add(buttonsRow);

        return panel;
    }

    private TextBlock BuildUnitLabel(string unit)
    {
        var text = new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppMuted"));
        return text;
    }
}
