namespace BrightnessControl.Core;

public interface IMonitorPacingStore
{
    int GetPacingMs(string monitorKey);

    void SetPacingMs(string monitorKey, int pacingMs);
}
