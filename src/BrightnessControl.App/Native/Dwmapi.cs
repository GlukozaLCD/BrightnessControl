using System.Runtime.InteropServices;

namespace BrightnessControl.App.Native;

internal static class Dwmapi
{
    // Возвращает "цвет колоризации" DWM (0xAARRGGBB) — на Windows 10/11 совпадает с
    // акцентным цветом, который пользователь выбрал в Параметры → Персонализация →
    // Цвета. Обычный Win32 P/Invoke без WinRT-зависимости (FP9 Фаза 7) — так же, как
    // и весь остальной нативный код в проекте (см. Core/Native/*).
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetColorizationColor(out uint colorizationColor, out bool opaqueBlend);
}
