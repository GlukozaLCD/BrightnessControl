using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using BrightnessControl.App.Services;
using BrightnessControl.Core;

namespace BrightnessControl.App.Views;

// Стили HUD — по аналогии с TrayIconDesign (FP8): пользователь выбирает один
// из нескольких готовых визуальных стилей, а не подстраивает параметры
// вручную. Код хранится строкой в TraySettings.HudStyleId, а не голым enum,
// по той же причине (расширяемость без миграции сохранённых настроек).
public enum HudStyle
{
    GrowingRaysSun,
    CompactBar,
    PillToast,
}

public partial class BrightnessHudWindow : Window
{
    // ---- Growing Rays Sun — пропорции 1:1 с одобренным макетом (Artifact
    // "Circular Ring Refinement", viewBox 64, center 32, discR=14, лучи
    // 18→28), отмасштабированы на холст 130 (×2.03125). ----
    private const double SunCanvasSize = 130;
    private const double SunCenter = SunCanvasSize / 2;
    private const double DiscRadius = 28;
    private const double RayStart = 37;
    private const double RayEnd = 57;
    private const double RayLength = RayEnd - RayStart;
    // Точка на пороге — ПОЧТИ нулевой длины отрезок с круглыми концами: у
    // отрезка короче своей толщины оба круглых конца сливаются в ровный
    // круг. Строго 0 не берём — вдруг рендерер отбрасывает вырожденную
    // геометрию как невидимую.
    private const double MinDotLength = 0.1;
    private const double PrimaryStrokeThickness = 6.5;
    private const double SecondaryStrokeThickness = 5;

    private static readonly double[] PrimaryAnglesDeg = [0, 45, 90, 135, 180, 225, 270, 315];
    private static readonly double[] SecondaryAnglesDeg = [22.5, 67.5, 112.5, 157.5, 202.5, 247.5, 292.5, 337.5];

    // ---- Compact Bar / Pill Toast — общие размеры карточки внутри окна. ----
    private const double BarTrackWidth = 130;

    private readonly TraySettings _traySettings;
    private readonly DispatcherTimer _hideTimer;

    private readonly Canvas _sunCanvas;
    private readonly Line[] _primaryRays;
    private readonly Line[] _secondaryRays;
    private readonly TextBlock _sunPercentText;

    private readonly Border _barRoot;
    private readonly Border _barFill;
    private readonly TextBlock _barPercentText;

    private readonly Border _pillRoot;
    private readonly TextBlock _pillPercentText;

    // Нужен только для XAML-дизайнера/превью — реальный экземпляр всегда
    // создаётся через конструктор ниже, с реальными настройками.
    public BrightnessHudWindow() : this(new TraySettings())
    {
    }

    public BrightnessHudWindow(TraySettings traySettings)
    {
        _traySettings = traySettings;
        InitializeComponent();

        var root = this.FindControl<Grid>("RootGrid")!;

        _sunCanvas = BuildSunCanvas(out _primaryRays, out _secondaryRays, out _sunPercentText);
        root.Children.Add(_sunCanvas);

        _barRoot = BuildCompactBar(out _barFill, out _barPercentText);
        root.Children.Add(_barRoot);

        _pillRoot = BuildPillToast(out _pillPercentText);
        root.Children.Add(_pillRoot);

        ApplyStyleSize(ParseStyle(_traySettings.HudStyleId));
        UpdateContent(0);

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

    private static HudStyle ParseStyle(string id) =>
        Enum.TryParse<HudStyle>(id, out var style) ? style : HudStyle.GrowingRaysSun;

    // ================= Growing Rays Sun =================

    private Canvas BuildSunCanvas(out Line[] primaryRays, out Line[] secondaryRays, out TextBlock percentText)
    {
        var canvas = new Canvas { Width = SunCanvasSize, Height = SunCanvasSize };

        var disc = new Ellipse { Width = DiscRadius * 2, Height = DiscRadius * 2 };
        disc.Bind(Shape.FillProperty, this.GetResourceObservable("AppAccentBrush"));
        Canvas.SetLeft(disc, SunCenter - DiscRadius);
        Canvas.SetTop(disc, SunCenter - DiscRadius);
        canvas.Children.Add(disc);

        // Вторичные лучи (FP13) красятся отдельным AppAccentOppositeBrush —
        // вычисляется из основного акцента по цветовой гармонии
        // (AccentColorService/ColorHarmony), а не тем же самым цветом, что и
        // диск/основные лучи.
        primaryRays = BuildRays(canvas, PrimaryAnglesDeg.Length, PrimaryStrokeThickness, "AppAccentBrush");
        secondaryRays = BuildRays(canvas, SecondaryAnglesDeg.Length, SecondaryStrokeThickness, "AppAccentOppositeBrush");

        // Число яркости — ЦИФРАМИ БЕЗ "%" (см. Фазу 3), поверх диска. Без
        // явных Width/Height — TextBlock не центрирует своё содержимое
        // внутри явно заданной большей области сам по себе (см. комментарий
        // у RepositionSunPercentText), поэтому центр считается вручную по
        // РЕАЛЬНО измеренному размеру текста.
        percentText = new TextBlock
        {
            Text = "0",
            FontSize = 26,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            LineHeight = 26,
        };
        percentText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppAccentOnBrush"));
        canvas.Children.Add(percentText);

        return canvas;
    }

    private Line[] BuildRays(Canvas canvas, int count, double strokeThickness, string strokeResourceKey)
    {
        var rays = new Line[count];
        for (var i = 0; i < count; i++)
        {
            var ray = new Line
            {
                StrokeThickness = strokeThickness,
                StrokeLineCap = Avalonia.Media.PenLineCap.Round,
            };
            ray.Bind(Shape.StrokeProperty, this.GetResourceObservable(strokeResourceKey));
            canvas.Children.Add(ray);
            rays[i] = ray;
        }

        return rays;
    }

    // Каретка центрирования: Canvas игнорирует Alignment для позиционирования
    // детей (важна только Canvas.Top/Left), а TextBlock с явно заданной
    // большей Height НЕ центрирует своё содержимое внутри неё сам по себе —
    // раньше это давало видимое смещение числа от центра диска (та же
    // категория бага, что и смещение "+"/"×" — см. п.8 плана).
    private void RepositionSunPercentText()
    {
        _sunPercentText.Measure(Size.Infinity);
        var size = _sunPercentText.DesiredSize;
        Canvas.SetLeft(_sunPercentText, SunCenter - size.Width / 2);
        Canvas.SetTop(_sunPercentText, SunCenter - size.Height / 2);
    }

    private static void SetRayLengths(Line[] rays, double[] anglesDeg, double length)
    {
        for (var i = 0; i < rays.Length; i++)
        {
            var radians = anglesDeg[i] * Math.PI / 180.0;
            var dx = Math.Cos(radians);
            var dy = Math.Sin(radians);

            rays[i].IsVisible = length > 0;
            rays[i].StartPoint = new Point(SunCenter + dx * RayStart, SunCenter + dy * RayStart);
            rays[i].EndPoint = new Point(SunCenter + dx * (RayStart + length), SunCenter + dy * (RayStart + length));
        }
    }

    private static double GrowFromDot(int percent, double thresholdStart, double thresholdFull)
    {
        if (percent < thresholdStart)
        {
            return 0;
        }

        var growth = Math.Clamp((percent - thresholdStart) / (thresholdFull - thresholdStart), 0, 1);
        return Math.Max(MinDotLength, growth * RayLength);
    }

    // Непрерывный рост на ВСЁМ диапазоне, без пауз (уточнено пользователем
    // 2026-09-07 — версия с порогами 25%/75% давала "мёртвую зону" 50-75%):
    // основные лучи растут 0→50% (на 0% истинный ноль, без кружка),
    // дополнительные — 50→100% (кружок ровно в момент старта на 50%).
    private void UpdateRays(int percent)
    {
        var primaryGrowth = Math.Clamp(percent / 50.0, 0, 1);
        SetRayLengths(_primaryRays, PrimaryAnglesDeg, primaryGrowth * RayLength);

        SetRayLengths(_secondaryRays, SecondaryAnglesDeg, GrowFromDot(percent, 50, 100));
    }

    // ================= Compact Bar =================

    private Border BuildCompactBar(out Border fill, out TextBlock percentText)
    {
        var track = new Border
        {
            Width = BarTrackWidth,
            Height = 10,
            CornerRadius = new CornerRadius(5),
        };
        // FP13: незалитая часть шкалы — приглушённый акцентный тинт
        // (AppAccentBackgroundVariantBrush, специально приглушён по
        // насыщенности под фоновые поверхности), а не нейтральный серый —
        // тонкая привязка к выбранной комбинации даже там, где элемент
        // формально "пустой"/неактивный. Насыщенный AppAccentOppositeBrush
        // сюда не подходит — он рассчитан на яркие акценты переднего плана
        // (лучи HUD-солнца), а не на спокойный фоновый элемент.
        track.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppAccentBackgroundVariantBrush"));

        fill = new Border
        {
            Width = 0,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        fill.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppAccentBrush"));

        var trackHost = new Panel { Width = BarTrackWidth, Height = 10, VerticalAlignment = VerticalAlignment.Center };
        trackHost.Children.Add(track);
        trackHost.Children.Add(fill);

        percentText = new TextBlock
        {
            Text = "0",
            FontSize = 20,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(4, 0, 0, 0),
        };
        percentText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppInk"));

        // StackPanel с HorizontalAlignment=Center двигал ШКАЛУ ЦЕЛИКОМ при
        // смене ширины числа ("7" короче "100") — вся группа перецентровывалась
        // как один блок, из-за чего левый край шкалы "гулял". Grid с колонкой
        // Auto под шкалу и * под число фиксирует шкалу на месте: левая колонка
        // всегда той же ширины (сама шкала не меняется), а правая тянется —
        // меняется только зазор ПЕРЕД числом, а не позиция самой шкалы.
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(trackHost, 0);
        Grid.SetColumn(percentText, 1);
        grid.Children.Add(trackHost);
        grid.Children.Add(percentText);

        var card = new Border
        {
            Child = grid,
            Margin = new Thickness(8),
            Padding = new Thickness(14, 0, 10, 0),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
        };
        // FP13: карточка получает приглушённый акцентный тинт вместо
        // нейтрального AppSurface — Compact Bar и Pill Toast намеренно берут
        // РАЗНЫЕ тона одной фоновой пары (Background/BackgroundVariant), а не
        // один и тот же, чтобы стили визуально отличались друг от друга.
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppAccentBackgroundBrush"));
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppLineStrong"));

        return card;
    }

    // ================= Pill Toast =================

    // Максимально минималистично (уточнено пользователем 2026-09-07, после
    // первой версии с зелёной точкой-индикатором — убрана как лишнее
    // украшение): ничего, кроме самого числа, никаких доп. значков.
    private Border BuildPillToast(out TextBlock percentText)
    {
        percentText = new TextBlock
        {
            Text = "0",
            FontSize = 24,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        percentText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AppAccentBrush"));

        var card = new Border
        {
            Child = percentText,
            Margin = new Thickness(8),
            // Радиус вручную под половину высоты карточки (64-16=48, /2=24) —
            // настоящая капсула, а не просто скруглённый прямоугольник.
            CornerRadius = new CornerRadius(24),
            BorderThickness = new Thickness(1),
        };
        // FP13: второй тон той же фоновой пары, что и у Compact Bar (см.
        // BuildCompactBar) — намеренно другой, а не AppAccentBackgroundBrush,
        // чтобы стили визуально отличались.
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppAccentBackgroundVariantBrush"));
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("AppLineStrong"));

        return card;
    }

    // ================= Общее =================

    private void ApplyStyleSize(HudStyle style)
    {
        (Width, Height) = style switch
        {
            HudStyle.CompactBar => (232.0, 72.0),
            HudStyle.PillToast => (166.0, 64.0),
            _ => (SunCanvasSize, SunCanvasSize),
        };
    }

    private void UpdateContent(int percent)
    {
        var style = ParseStyle(_traySettings.HudStyleId);

        _sunCanvas.IsVisible = style == HudStyle.GrowingRaysSun;
        _barRoot.IsVisible = style == HudStyle.CompactBar;
        _pillRoot.IsVisible = style == HudStyle.PillToast;

        switch (style)
        {
            case HudStyle.GrowingRaysSun:
                _sunPercentText.Text = percent.ToString();
                RepositionSunPercentText();
                UpdateRays(percent);
                break;
            case HudStyle.CompactBar:
                _barPercentText.Text = percent.ToString();
                _barFill.Width = BarTrackWidth * Math.Clamp(percent, 0, 100) / 100.0;
                break;
            case HudStyle.PillToast:
                _pillPercentText.Text = percent.ToString();
                break;
        }
    }

    // Показывает процент и перезапускает таймер автоскрытия — вызывается на
    // каждый "тик" колеса, пока пользователь крутит его над иконкой трея.
    // Позиция — та же общая логика, что и у SettingsWindow/GlobalSliderPopup
    // (см. TrayPopupPlacement): прижато к краю монитора/панели задач, а не
    // просто "рядом с курсором" — раньше HUD мог оказаться где угодно у самого
    // края экрана вместе с курсором.
    public void ShowPercent(int percent, MonitorBounds iconRect)
    {
        ApplyStyleSize(ParseStyle(_traySettings.HudStyleId));
        UpdateContent(percent);

        if (!IsVisible)
        {
            Show();
        }

        Position = TrayPopupPlacement.Compute(Screens, iconRect.X, iconRect.Y, iconRect.Width, iconRect.Height, (int)Width, (int)Height);

        _hideTimer.Stop();
        _hideTimer.Start();
    }
}
