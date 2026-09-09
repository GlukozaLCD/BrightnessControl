namespace BrightnessControl.Core;

// Простой отрезок времени внутри суток в минутах от полуночи (0..1440) — НЕ
// TimeOnly, потому что TimeOnly физически не может хранить значение "24:00"
// (диапазон только 00:00:00–23:59:59.9999999). Без этого отрезок "до полуночи"
// терял бы последние минуты суток при попытке представить его границу (см.
// PLAN_FP10 Фаза 4). StartMinute всегда < EndMinute — оборот через полночь
// выражается ДВУМЯ отдельными отрезками (см. AutomationRule.TimeSegments), а
// не оборачивающимся диапазоном, как было у старого TimeRange.
public sealed class TimeSegment
{
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }

    public bool Contains(int minuteOfDay) => minuteOfDay >= StartMinute && minuteOfDay < EndMinute;

    // Сортирует и сливает пересекающиеся/касающиеся отрезки — тот же алгоритм,
    // что в UI-макете линейки. Если результат схлопывается РОВНО в один отрезок
    // на все сутки [0,1440], возвращает isAlwaysActive=true и ПУСТОЙ список —
    // единственный источник истины для "всегда", а не два (см. PLAN_FP10 Фаза 4:
    // это ещё и обходит невозможность честно хранить границу "24:00").
    public static (List<TimeSegment> Segments, bool IsAlwaysActive) Normalize(IEnumerable<TimeSegment> segments)
    {
        var sorted = segments
            .Where(s => s.EndMinute > s.StartMinute)
            .Select(s => new TimeSegment { StartMinute = Math.Max(0, s.StartMinute), EndMinute = Math.Min(1440, s.EndMinute) })
            .OrderBy(s => s.StartMinute)
            .ToList();

        var merged = new List<TimeSegment>();
        foreach (var seg in sorted)
        {
            if (merged.Count > 0 && seg.StartMinute <= merged[^1].EndMinute)
            {
                merged[^1].EndMinute = Math.Max(merged[^1].EndMinute, seg.EndMinute);
            }
            else
            {
                merged.Add(seg);
            }
        }

        if (merged.Count == 1 && merged[0].StartMinute <= 0 && merged[0].EndMinute >= 1440)
        {
            return (new List<TimeSegment>(), true);
        }

        return (merged, false);
    }
}
