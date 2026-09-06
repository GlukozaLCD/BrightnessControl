namespace BrightnessControl.Core;

public interface IIdleSettingsStore
{
    IdleSettings Load();

    void Save(IdleSettings settings);
}
