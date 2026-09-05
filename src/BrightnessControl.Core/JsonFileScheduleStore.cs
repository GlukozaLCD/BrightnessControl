using System.Text.Json;

namespace BrightnessControl.Core;

public sealed class JsonFileScheduleStore : IScheduleStore
{
    private readonly string _filePath;

    public JsonFileScheduleStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrightnessControl",
            "schedule.json");
    }

    public ScheduleSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new ScheduleSettings();
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<ScheduleSettings>(json) ?? new ScheduleSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ScheduleSettings();
        }
    }

    public void Save(ScheduleSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
