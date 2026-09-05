namespace BrightnessControl.Core;

public interface IForegroundAppWatcher : IDisposable
{
    event Action<ForegroundAppInfo>? ForegroundChanged;

    // Вызывать ПОСЛЕ подписки на ForegroundChanged — см. реализацию в ForegroundAppWatcher.
    void ReportCurrentForegroundWindow();
}
