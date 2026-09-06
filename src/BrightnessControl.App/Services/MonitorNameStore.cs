using System.Text.Json;

namespace BrightnessControl.App.Services;

// Пользовательские названия мониторов (переименование в настройках) — ключ,
// как и везде в проекте, это BrightnessController.GetMonitorKey(monitor), не
// индекс/название по умолчанию (та же логика идентификации, что уже
// используется для яркости, расписания, профилей приложений).
// Расположение файла — %LocalAppData% (свой на каждого пользователя Windows),
// как и остальные хранилища проекта; выбор portable-режима (FP7, пока не
// реализован ни для одного стора в проекте) сможет позже передать сюда общий
// filePath для всех пользователей сразу — конструктор уже принимает filePath
// явно именно ради этого, тем же способом, что и другие Store-классы.
public sealed class MonitorNameStore
{
    private readonly string _filePath;

    public MonitorNameStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrightnessControl",
            "monitor-names.json");
    }

    public Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new Dictionary<string, string>();
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Dictionary<string, string>();
        }
    }

    public void Save(Dictionary<string, string> names)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(names));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
