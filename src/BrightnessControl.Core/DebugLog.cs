namespace BrightnessControl.Core;

// Временный диагностический лог для разбора нестабильного поведения DDC/CI у
// конкретных мониторов — не постоянная логическая подсистема продукта.
public static class DebugLog
{
    private static readonly object Lock = new();
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BrightnessControl",
        "debug.log");

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} [T{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
