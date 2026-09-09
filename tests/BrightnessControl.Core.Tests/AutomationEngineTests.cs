namespace BrightnessControl.Core.Tests;

public class AutomationEngineTests
{
    private static readonly MonitorInfo Monitor1 = new(
        "DEVICE1", "Monitor 1", "\\\\.\\DISPLAY1", MonitorConnectionKind.ExternalDdcCi, new MonitorBounds(0, 0, 1920, 1080));

    private static readonly MonitorInfo Monitor2 = new(
        "DEVICE2", "Monitor 2", "\\\\.\\DISPLAY2", MonitorConnectionKind.ExternalDdcCi, new MonitorBounds(1920, 0, 1920, 1080));

    private const string Monitor1Key = "device1";
    private const string Monitor2Key = "device2";

    private static AutomationSettings Settings(params AutomationRule[] rules) => new() { IsEnabled = true, Rules = rules.ToList() };

    // Правило "22:00 → 06:00 через полночь" теперь выражается ДВУМЯ отдельными
    // отрезками (см. PLAN_FP10 Фаза 4), а не одним оборачивающимся диапазоном.
    private static List<TimeSegment> OvernightSegments() => new()
    {
        new TimeSegment { StartMinute = 22 * 60, EndMinute = 1440 },
        new TimeSegment { StartMinute = 0, EndMinute = 6 * 60 },
    };

    [Fact]
    public void TimeSegment_Contains_BasicRange()
    {
        var seg = new TimeSegment { StartMinute = 9 * 60, EndMinute = 17 * 60 };

        Assert.True(seg.Contains(12 * 60));
        Assert.False(seg.Contains(8 * 60));
        Assert.False(seg.Contains(17 * 60));
    }

    [Fact]
    public void TimeSegment_Normalize_MergesOverlappingAndTouchingSegments()
    {
        var input = new[]
        {
            new TimeSegment { StartMinute = 22 * 60, EndMinute = 1440 },
            new TimeSegment { StartMinute = 0, EndMinute = 6 * 60 },
            new TimeSegment { StartMinute = 6 * 60, EndMinute = 8 * 60 }, // касается предыдущего
        };

        var (segments, isAlwaysActive) = TimeSegment.Normalize(input);

        // Normalize сортирует по StartMinute — отрезок с началом в 00:00 идёт первым.
        Assert.False(isAlwaysActive);
        Assert.Equal(2, segments.Count);
        Assert.Equal(0, segments[0].StartMinute);
        Assert.Equal(8 * 60, segments[0].EndMinute);
        Assert.Equal(22 * 60, segments[1].StartMinute);
        Assert.Equal(1440, segments[1].EndMinute);
    }

    [Fact]
    public void TimeSegment_Normalize_CollapsesFullDayToAlwaysActive()
    {
        var input = new[]
        {
            new TimeSegment { StartMinute = 0, EndMinute = 12 * 60 },
            new TimeSegment { StartMinute = 12 * 60, EndMinute = 1440 },
        };

        var (segments, isAlwaysActive) = TimeSegment.Normalize(input);

        Assert.True(isAlwaysActive);
        Assert.Empty(segments);
    }

    [Fact]
    public void TimeSegment_Normalize_DropsZeroOrNegativeWidthSegments()
    {
        var input = new[]
        {
            new TimeSegment { StartMinute = 100, EndMinute = 100 },
            new TimeSegment { StartMinute = 200, EndMinute = 150 },
            new TimeSegment { StartMinute = 300, EndMinute = 400 },
        };

        var (segments, isAlwaysActive) = TimeSegment.Normalize(input);

        Assert.False(isAlwaysActive);
        Assert.Single(segments);
        Assert.Equal(300, segments[0].StartMinute);
        Assert.Equal(400, segments[0].EndMinute);
    }

    [Fact]
    public void AutomationRule_MatchesTime_TwoSegmentsRepresentWrapAroundMidnight()
    {
        var rule = new AutomationRule { TimeSegments = OvernightSegments() };

        Assert.True(rule.MatchesTime(23 * 60));
        Assert.True(rule.MatchesTime(2 * 60));
        Assert.False(rule.MatchesTime(12 * 60));
    }

    [Fact]
    public void AutomationRule_MatchesTime_AlwaysActive_IgnoresSegments()
    {
        var rule = new AutomationRule { IsTimeAlwaysActive = true };

        Assert.True(rule.MatchesTime(0));
        Assert.True(rule.MatchesTime(1439));
    }

    [Fact]
    public void ResolveWinner_TimeOnlyRule_AppliesWithinRange_RespectsStaticScope()
    {
        var rule = new AutomationRule
        {
            TimeSegments = { new TimeSegment { StartMinute = 9 * 60, EndMinute = 17 * 60 } },
            Percent = 60,
            MonitorKeys = { Monitor1Key },
        };
        var settings = Settings(rule);

        var winnerOnScopedMonitor = AutomationEngine.ResolveWinner(settings, Monitor1, Monitor1Key, foreground: null, TimeOnly.Parse("12:00"));
        var winnerOnOtherMonitor = AutomationEngine.ResolveWinner(settings, Monitor2, Monitor2Key, foreground: null, TimeOnly.Parse("12:00"));
        var winnerOutsideRange = AutomationEngine.ResolveWinner(settings, Monitor1, Monitor1Key, foreground: null, TimeOnly.Parse("20:00"));

        Assert.Same(rule, winnerOnScopedMonitor);
        Assert.Null(winnerOnOtherMonitor);
        Assert.Null(winnerOutsideRange);
    }

    [Fact]
    public void ResolveWinner_ProcessOnlyRule_AppliesOnlyToMonitorWithMatchedWindow()
    {
        var rule = new AutomationRule
        {
            ProcessMatchType = AppMatchType.ProcessName,
            ProcessMatchValue = "notepad",
            Percent = 40,
        };
        var settings = Settings(rule);
        var foreground = new ForegroundAppInfo("notepad", "Untitled - Notepad", Monitor1.AdapterDeviceName);

        var winnerOnMatchedMonitor = AutomationEngine.ResolveWinner(settings, Monitor1, Monitor1Key, foreground, TimeOnly.Parse("12:00"));
        var winnerOnOtherMonitor = AutomationEngine.ResolveWinner(settings, Monitor2, Monitor2Key, foreground, TimeOnly.Parse("12:00"));
        var winnerWhenNotRunning = AutomationEngine.ResolveWinner(settings, Monitor1, Monitor1Key, foreground: null, TimeOnly.Parse("12:00"));

        Assert.Same(rule, winnerOnMatchedMonitor);
        Assert.Null(winnerOnOtherMonitor);
        Assert.Null(winnerWhenNotRunning);
    }

    [Fact]
    public void ResolveWinner_CombinedAnd_RequiresBothConditions()
    {
        var rule = new AutomationRule
        {
            TimeSegments = OvernightSegments(),
            ProcessMatchType = AppMatchType.ProcessName,
            ProcessMatchValue = "game",
            Combinator = AutomationCombinator.And,
            Percent = 30,
        };
        var settings = Settings(rule);
        var foreground = new ForegroundAppInfo("game", "Game", Monitor1.AdapterDeviceName);

        var bothTrue = AutomationEngine.ResolveWinner(settings, Monitor1, Monitor1Key, foreground, TimeOnly.Parse("23:00"));
        var onlyTimeTrue = AutomationEngine.ResolveWinner(settings, Monitor1, Monitor1Key, foreground: null, TimeOnly.Parse("23:00"));
        var onlyProcessTrue = AutomationEngine.ResolveWinner(settings, Monitor1, Monitor1Key, foreground, TimeOnly.Parse("12:00"));

        Assert.Same(rule, bothTrue);
        Assert.Null(onlyTimeTrue);
        Assert.Null(onlyProcessTrue);
    }

    [Fact]
    public void ResolveWinner_CombinedOr_ScopeFollowsWhicheverConditionFired()
    {
        var rule = new AutomationRule
        {
            TimeSegments = OvernightSegments(),
            ProcessMatchType = AppMatchType.ProcessName,
            ProcessMatchValue = "game",
            Combinator = AutomationCombinator.Or,
            Percent = 30,
            MonitorKeys = { Monitor1Key },
        };
        var settings = Settings(rule);
        var foregroundOnMonitor2 = new ForegroundAppInfo("game", "Game", Monitor2.AdapterDeviceName);

        // Время сработало (правило активно), процесс — нет: область действия
        // статичная (MonitorKeys), поэтому на Monitor2 (не в списке) правило не применяется.
        var timeOnlyOnMonitor2 = AutomationEngine.ResolveWinner(settings, Monitor2, Monitor2Key, foreground: null, TimeOnly.Parse("23:00"));
        var timeOnlyOnMonitor1 = AutomationEngine.ResolveWinner(settings, Monitor1, Monitor1Key, foreground: null, TimeOnly.Parse("23:00"));

        // Процесс сработал (на Monitor2): область действия динамическая, значит
        // применяется именно к Monitor2, несмотря на то что его нет в MonitorKeys.
        var processOnlyOnMonitor2 = AutomationEngine.ResolveWinner(settings, Monitor2, Monitor2Key, foregroundOnMonitor2, TimeOnly.Parse("12:00"));

        Assert.Null(timeOnlyOnMonitor2);
        Assert.Same(rule, timeOnlyOnMonitor1);
        Assert.Same(rule, processOnlyOnMonitor2);
    }

    [Fact]
    public void ResolveWinner_ExplicitPriority_BeatsRuleWithoutPriority()
    {
        var lowPriorityProcessRule = new AutomationRule
        {
            ProcessMatchType = AppMatchType.ProcessName,
            ProcessMatchValue = "game",
            Percent = 30,
        };
        var highPriorityTimeRule = new AutomationRule
        {
            IsTimeAlwaysActive = true,
            Percent = 80,
            Priority = 100,
        };
        var settings = Settings(lowPriorityProcessRule, highPriorityTimeRule);
        var foreground = new ForegroundAppInfo("game", "Game", Monitor1.AdapterDeviceName);

        var winner = AutomationEngine.ResolveWinner(settings, Monitor1, Monitor1Key, foreground, TimeOnly.Parse("12:00"));

        Assert.Same(highPriorityTimeRule, winner);
    }

    [Fact]
    public void ResolveWinner_WithoutExplicitPriority_ProcessRuleBeatsTimeOnlyRule()
    {
        var timeRule = new AutomationRule
        {
            IsTimeAlwaysActive = true,
            Percent = 80,
        };
        var processRule = new AutomationRule
        {
            ProcessMatchType = AppMatchType.ProcessName,
            ProcessMatchValue = "game",
            Percent = 30,
        };
        var settings = Settings(timeRule, processRule);
        var foreground = new ForegroundAppInfo("game", "Game", Monitor1.AdapterDeviceName);

        var winner = AutomationEngine.ResolveWinner(settings, Monitor1, Monitor1Key, foreground, TimeOnly.Parse("12:00"));

        Assert.Same(processRule, winner);
    }

    [Fact]
    public void ResolveWinner_EqualRank_EarlierRuleInListWins()
    {
        var first = new AutomationRule { IsTimeAlwaysActive = true, Percent = 50 };
        var second = new AutomationRule { IsTimeAlwaysActive = true, Percent = 90 };
        var settings = Settings(first, second);

        var winner = AutomationEngine.ResolveWinner(settings, Monitor1, Monitor1Key, foreground: null, TimeOnly.Parse("12:00"));

        Assert.Same(first, winner);
    }

    [Fact]
    public void ResolveWinner_DisabledRule_IsIgnored()
    {
        var rule = new AutomationRule { IsEnabled = false, IsTimeAlwaysActive = true, Percent = 50 };
        var settings = Settings(rule);

        var winner = AutomationEngine.ResolveWinner(settings, Monitor1, Monitor1Key, foreground: null, TimeOnly.Parse("12:00"));

        Assert.Null(winner);
    }
}
