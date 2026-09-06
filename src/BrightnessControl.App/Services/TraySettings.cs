namespace BrightnessControl.App.Services;

public sealed class TraySettings
{
    public int ScrollStepPercent { get; set; } = 10;
    public bool IsScrollEnabled { get; set; } = true;

    // "Липкие" значения при скролле над треем: при прохождении рядом с одним из
    // них скролл на нём один "тик" задерживается, а не проскакивает мимо — но
    // любые другие проценты остаются доступны как обычно, без ограничений.
    public List<int> StickyValues { get; set; } = new();

    // Форма/цвет/масштаб иконки трея — независимые параметры, иконка рисуется
    // на лету (см. TrayIconRenderer), а не грузится из готового файла. Код
    // формы (TrayIconDesignId) — строка, а не сам enum, чтобы имя типа можно
    // было менять/расширять (в т.ч. пользовательскими иконками в будущем), не
    // ломая уже сохранённые файлы настроек.
    public string TrayIconDesignId { get; set; } = TrayIconDesign.Spokes.ToString();
    public string TrayIconColorHex { get; set; } = "#F2900C";

    // Масштаб — свой на каждую форму (код формы → процент), крутится колесом
    // мыши прямо над карточкой формы в галерее, а не общим слайдером на все
    // формы сразу.
    public Dictionary<string, int> TrayIconScaleByDesign { get; set; } = new();

    // Пользовательское переименование формы (код формы → новое отображаемое
    // имя), поверх названия по умолчанию из TrayIconCatalog — так название
    // можно спокойно поменять, не трогая сам код формы.
    public Dictionary<string, string> TrayIconDesignNameOverrides { get; set; } = new();

    // Палитра цветов иконки — полностью в руках пользователя (добавление
    // своего цвета, удаление, любое количество), а не 4 зашитых варианта.
    // Значения по умолчанию — только "затравка" для НОВОГО файла настроек.
    public List<string> TrayIconColors { get; set; } = ["#FFFFFF", "#FFDD00", "#F2900C", "#E53935"];

    // Порядок карточек форм в галерее (список кодов формы) — задаётся кнопками
    // "влево/вправо" прямо в галерее. Формы, ещё не встречавшиеся в этом списке
    // (новые, добавленные уже после того как список сохранился), добавляются в
    // конец в порядке TrayIconCatalog.Designs — см. SettingsWindow.GetOrderedDesigns.
    public List<string> TrayIconDesignOrder { get; set; } = new();

    public int GetTrayIconScale(string designId) =>
        TrayIconScaleByDesign.TryGetValue(designId, out var value) ? value : 135;
}
