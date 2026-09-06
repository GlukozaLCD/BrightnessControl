using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using BrightnessControl.App.Native;

namespace BrightnessControl.App.Views;

// Общая логика позиционирования всех окон/поповеров, привязанных к иконке трея
// (SettingsWindow, GlobalSliderPopup, BrightnessHudWindow) — сторона прижатия
// определяется АВТОМАТИЧЕСКИ через положение реальной панели задач
// (SHAppBarMessage/ABM_GETTASKBARPOS — тот же Shell API, которым пользуется сам
// Explorer для этой цели), а не ручной настройкой: для горизонтальной панели
// (низ/верх экрана, обычный случай) окно прижимается к тому краю МОНИТОРА, что
// ближе к иконке, и открывается с противоположной от панели стороны по
// вертикали; для вертикальной панели (лево/право) — открывается сбоку от самой
// панели (не поверх неё), а по вертикали прижимается к ближайшему к иконке
// краю. Монитор — тот, где физически находится иконка, не обязательно
// основной — окно всегда остаётся на экране, откуда по ней кликнули/крутили.
public static class TrayPopupPlacement
{
    private const int EdgeMargin = 10;

    public static PixelPoint Compute(Screens? screens, int iconX, int iconY, int iconWidth, int iconHeight, int windowWidth, int windowHeight)
    {
        var screen = screens?.ScreenFromPoint(new PixelPoint(iconX, iconY)) ?? screens?.Primary;
        if (screen is null)
        {
            // Монитор не определился (крайне маловероятно) — просто ставим окно
            // чуть выше и левее иконки, без прижатия к краю.
            var anchorX = iconX + iconWidth;
            var anchorY = iconY - EdgeMargin;
            return new PixelPoint(anchorX - windowWidth, anchorY - windowHeight);
        }

        var area = screen.WorkingArea;

        var taskbarDetected = TryGetTaskbarPosition(out var taskbarEdge, out var taskbarRect);
        var isVerticalTaskbar = taskbarDetected && taskbarEdge is Shell32.ABE_LEFT or Shell32.ABE_RIGHT;
        var isTopTaskbar = taskbarDetected && taskbarEdge == Shell32.ABE_TOP;

        int x, y;
        if (isVerticalTaskbar)
        {
            // Вертикальная панель — открываем СБОКУ от неё, а не поверх.
            x = taskbarEdge == Shell32.ABE_LEFT
                ? taskbarRect.Right + EdgeMargin
                : taskbarRect.Left - windowWidth - EdgeMargin;

            var closerToTop = iconY - area.Y < area.Y + area.Height - iconY;
            y = closerToTop ? area.Y + EdgeMargin : area.Y + area.Height - windowHeight - EdgeMargin;
        }
        else
        {
            // Горизонтальная панель (обычный случай) или не удалось определить —
            // прижимаемся по X к тому краю монитора, что ближе к иконке.
            var closerToLeft = iconX - area.X < area.X + area.Width - iconX;
            x = closerToLeft ? area.X + EdgeMargin : area.X + area.Width - windowWidth - EdgeMargin;

            y = isTopTaskbar
                ? iconY + iconHeight + EdgeMargin
                : iconY - EdgeMargin - windowHeight;
        }

        var minY = area.Y;
        var maxY = Math.Max(minY, area.Y + area.Height - windowHeight);
        y = Math.Clamp(y, minY, maxY);

        var minX = area.X;
        var maxX = Math.Max(minX, area.X + area.Width - windowWidth);
        x = Math.Clamp(x, minX, maxX);

        return new PixelPoint(x, y);
    }

    // Оборачивает SHAppBarMessage(ABM_GETTASKBARPOS) — официальный способ узнать
    // геометрию и сторону панели задач. Возвращает false, если Shell почему-то не
    // ответил (например, нестандартная замена шелла без Shell_TrayWnd) — вызывающий
    // код в этом случае просто использует консервативный дефолт (трактует как
    // обычную нижнюю панель).
    private static bool TryGetTaskbarPosition(out uint edge, out Shell32.RECT rect)
    {
        var data = new Shell32.APPBARDATA { cbSize = (uint)Marshal.SizeOf<Shell32.APPBARDATA>() };
        var result = Shell32.SHAppBarMessage(Shell32.ABM_GETTASKBARPOS, ref data);

        edge = data.uEdge;
        rect = data.rc;
        return result != IntPtr.Zero;
    }
}
