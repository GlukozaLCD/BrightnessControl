namespace BrightnessControl.Core;

public interface IBrightnessStateStore
{
    int? GetLastPercent(string monitorKey);

    void SetLastPercent(string monitorKey, int percent);
}
