namespace ToolsBox.Core.WorkCountdown;

public static class ChinaWorkCalendar
{
    // 国务院办公厅关于2026年部分节假日安排的通知：
    // https://www.beijing.gov.cn/fuwu/bmfw/sy/jrts/202511/t20251104_4258838.html
    private static readonly (int Month, int First, int Last, string Name)[] Holidays =
    [
        (1, 1, 3, "元旦"),
        (2, 15, 23, "春节"),
        (4, 4, 6, "清明节"),
        (5, 1, 5, "劳动节"),
        (6, 19, 21, "端午节"),
        (9, 25, 27, "中秋节"),
        (10, 1, 7, "国庆节")
    ];

    private static readonly HashSet<DateOnly> MakeupWorkdays =
    [
        new(2026, 1, 4), new(2026, 2, 14), new(2026, 2, 28),
        new(2026, 5, 9), new(2026, 9, 20), new(2026, 10, 10)
    ];

    public static DayClassification GetDay(DateOnly date)
    {
        if (date.Year != 2026)
            return new DayClassification(DayKind.Unknown, "暂无该年度节假日数据，请手动确认");

        if (MakeupWorkdays.Contains(date))
            return new DayClassification(DayKind.Workday, "调休补班日");

        foreach (var holiday in Holidays)
            if (date.Month == holiday.Month && date.Day >= holiday.First && date.Day <= holiday.Last)
                return new DayClassification(DayKind.RestDay, $"{holiday.Name}假期");

        return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? new DayClassification(DayKind.RestDay, "周末休息日")
            : new DayClassification(DayKind.Workday, "工作日");
    }
}
