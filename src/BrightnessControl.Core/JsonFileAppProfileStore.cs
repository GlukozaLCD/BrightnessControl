using System.Text.Json;

namespace BrightnessControl.Core;

public sealed class JsonFileAppProfileStore : IAppProfileStore
{
    private readonly string _filePath;

    public JsonFileAppProfileStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrightnessControl",
            "app-profiles.json");
    }

    public AppProfileSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppProfileSettings();
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppProfileSettings>(json) ?? new AppProfileSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppProfileSettings();
        }
    }

    public void Save(AppProfileSettings settings)
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
