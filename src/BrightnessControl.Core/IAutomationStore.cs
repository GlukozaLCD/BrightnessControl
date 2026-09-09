namespace BrightnessControl.Core;

public interface IAutomationStore
{
    AutomationSettings Load();

    void Save(AutomationSettings settings);
}
