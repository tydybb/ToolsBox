namespace ToolsBox.Core.WorkCountdown;

/// <summary>下班庆祝特效分级。</summary>
public enum CelebrationLevel
{
    /// <summary>与假期无关的日子，不放特效。</summary>
    None,
    /// <summary>3~6 天的假期：轻量版，只撒纸屑。</summary>
    Light,
    /// <summary>7 天及以上的长假：完整版，纸屑 + 烟花。</summary>
    Full
}

/// <summary>
/// 判定某天下班要不要放庆祝特效，以及放多重。
/// 只在两种日子放：节前最后一个工作日（次日即假期首日、且今天是工作日）和法定假期内。
/// 再按该假期的总天数分级：7 天及以上的长假（春节、国庆）完整版，其余假期轻量版。
/// 年份无节假日数据时一律返回 None。
/// </summary>
public static class HolidayCelebration
{
    private const int FullVersionDays = 7;

    public static CelebrationLevel Detect(DateOnly date)
    {
        HolidaySpan? span = ChinaWorkCalendar.GetHolidayAt(date);
        // 与 HolidayFunCopy.Detect 的节前判断保持一致：只有工作日才算“节前最后一个工作日”，
        // 周末哪怕次日放假也不放特效（那天本来就不上班）。
        if (span is null && ChinaWorkCalendar.GetDay(date).Kind == DayKind.Workday)
            span = ChinaWorkCalendar.GetHolidayAt(date.AddDays(1));
        if (span is not HolidaySpan found) return CelebrationLevel.None;

        int days = found.End.DayNumber - found.Start.DayNumber + 1;
        return days >= FullVersionDays ? CelebrationLevel.Full : CelebrationLevel.Light;
    }
}
