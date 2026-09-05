namespace BrightnessControl.Core;

public interface IScheduleStore
{
    ScheduleSettings Load();

    void Save(ScheduleSettings settings);
}
