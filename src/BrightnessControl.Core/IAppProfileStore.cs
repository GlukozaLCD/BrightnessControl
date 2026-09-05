namespace BrightnessControl.Core;

public interface IAppProfileStore
{
    AppProfileSettings Load();

    void Save(AppProfileSettings settings);
}
