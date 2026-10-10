namespace ToolsBox.Core.WorkCountdown;

public enum HolidayMomentKind
{
    None,
    /// <summary>当天正在法定假期内（用户前提：假日还开软件就是在加班）。</summary>
    DuringHoliday,
    /// <summary>节前最后一个工作日，次日即假期首日。</summary>
    HolidayEve,
    /// <summary>国务院安排的调休补班日。</summary>
    MakeupWorkday,
    /// <summary>假期结束后的第一个工作日。</summary>
    AfterHoliday,
    /// <summary>距离下一个假期 7 天内。</summary>
    NearHoliday
}

/// <summary>
/// 节假日趣味文案场景快照。
/// Name：主假期名（DuringHoliday/HolidayEve 为当天或次日开始的假期，MakeupWorkday 为下一个假期，
/// AfterHoliday 为刚结束的假期，NearHoliday 为即将到来的假期）；
/// Days：DuringHoliday 表示假期第几天，其余场景表示距离天数（超出显示阈值时为 0）；
/// DaysLeft：假期余额，今天之后剩余天数，0 表示假期最后一天；
/// NextName：组合场景中的下一个假期名，无则为空串。
/// </summary>
public readonly record struct HolidayMoment(HolidayMomentKind Kind, string Name, int Days, int DaysLeft, string NextName);

/// <summary>
/// 按内置节假日日历生成倒计时与下班提示的趣味文案。
/// 无节日气氛或年份无日历数据时返回空串，调用方据此隐藏展示。
/// </summary>
public static class HolidayFunCopy
{
    private const int NearDays = 7;

    public static HolidayMoment Detect(DateOnly date)
    {
        if (ChinaWorkCalendar.GetHolidayAt(date) is HolidaySpan current)
            return new HolidayMoment(HolidayMomentKind.DuringHoliday, current.Name,
                date.DayNumber - current.Start.DayNumber + 1, current.End.DayNumber - date.DayNumber, "");

        if (ChinaWorkCalendar.GetHolidayAt(date.AddDays(1)) is HolidaySpan tomorrow
            && ChinaWorkCalendar.GetDay(date).Kind == DayKind.Workday)
            return new HolidayMoment(HolidayMomentKind.HolidayEve, tomorrow.Name, 0, 0, "");

        if (ChinaWorkCalendar.IsMakeupWorkday(date))
        {
            HolidaySpan? next = ChinaWorkCalendar.NextHolidayAfter(date);
            return new HolidayMoment(HolidayMomentKind.MakeupWorkday, next?.Name ?? "",
                next is HolidaySpan found ? found.Start.DayNumber - date.DayNumber : 0, 0, "");
        }

        if (ChinaWorkCalendar.GetHolidayAt(date.AddDays(-1)) is HolidaySpan ended
            && ChinaWorkCalendar.GetDay(date).Kind == DayKind.Workday)
        {
            HolidaySpan? next = ChinaWorkCalendar.NextHolidayAfter(date);
            int days = next is HolidaySpan upcoming ? upcoming.Start.DayNumber - date.DayNumber : 0;
            return new HolidayMoment(HolidayMomentKind.AfterHoliday, ended.Name,
                days is > 0 and <= NearDays ? days : 0, 0,
                days is > 0 and <= NearDays ? next!.Value.Name : "");
        }

        if (ChinaWorkCalendar.NextHolidayAfter(date) is HolidaySpan soon)
        {
            int days = soon.Start.DayNumber - date.DayNumber;
            if (days is > 0 and <= NearDays)
                return new HolidayMoment(HolidayMomentKind.NearHoliday, soon.Name, days, 0, "");
        }

        return new HolidayMoment(HolidayMomentKind.None, "", 0, 0, "");
    }

    public static string Countdown(HolidayMoment moment) => Countdown(moment, null);

    public static string Countdown(HolidayMoment moment, DateOnly? date) => WithTomorrow(moment.Kind switch
    {
        HolidayMomentKind.DuringHoliday => moment.DaysLeft == 0
            ? $"{moment.Name}假期最后一天，别人收心等开工，我收心继续加班，命苦"
            : $"{moment.Name}假期第 {moment.Days} 天，别人在旅游，我在加班，命苦，假期余额还剩 {moment.DaysLeft} 天",
        HolidayMomentKind.HolidayEve => $"明天开始{moment.Name}假期，今天是节前最后一个工作日，冲！",
        HolidayMomentKind.MakeupWorkday => moment.Name.Length == 0
            ? "今天是调休补班日，稳住节奏，忙完记得歇一歇"
            : $"今天是调休补班日，距离{moment.Name}假期还有 {moment.Days} 天，先把今天忙好",
        HolidayMomentKind.AfterHoliday => moment.NextName.Length == 0
            ? $"{moment.Name}假期余额已清零，节后第一天，缓一缓再冲。"
            : $"{moment.Name}假期余额已清零，{moment.NextName}假期还有 {moment.Days} 天到账！",
        HolidayMomentKind.NearHoliday => $"距离{moment.Name}假期还有 {moment.Days} 天，坚持住，放假在望！",
        _ => ""
    }, moment, date);

    public static string OffWork(HolidayMoment moment) => OffWork(moment, null);

    public static string OffWork(HolidayMoment moment, DateOnly? date) => WithTomorrow(moment.Kind switch
    {
        HolidayMomentKind.DuringHoliday => moment.DaysLeft == 0
            ? $"{moment.Name}假期最后一天收工，别人的假期结束了，我的班也加完了，命苦"
            : $"{moment.Name}假期第 {moment.Days} 天收工，别人放假我加班，假期余额还剩 {moment.DaysLeft} 天，命苦",
        HolidayMomentKind.HolidayEve => $"下班即放假，{moment.Name}假期我来啦！",
        HolidayMomentKind.MakeupWorkday => moment.Name.Length == 0
            ? "调休补班收工，把今晚留给生活"
            : $"调休补班收工，距离{moment.Name}假期还有 {DaysAfterToday(moment)} 天",
        HolidayMomentKind.AfterHoliday => moment.NextName.Length == 0
            ? "假期余额已清零，打工人回归，且行且珍惜。"
            : $"收工！{moment.Name}假期余额已清零，{moment.NextName}假期还有 {DaysAfterToday(moment)} 天到账",
        HolidayMomentKind.NearHoliday => $"收工！距离{moment.Name}假期还有 {DaysAfterToday(moment)} 天，假期在望",
        _ => ""
    }, moment, date);

    private static string WithTomorrow(string copy, HolidayMoment moment, DateOnly? date)
    {
        if (date is not DateOnly workDate
            || (moment.Kind != HolidayMomentKind.MakeupWorkday
                && !(moment.Kind == HolidayMomentKind.DuringHoliday && moment.DaysLeft == 0)))
            return copy;
        string tomorrow = WeekdayFunCopy.TomorrowDescription(workDate);
        return tomorrow.Length == 0 ? copy : $"{copy}，{tomorrow}";
    }

    /// <summary>
    /// 收工文案里的距离天数：今天已经过完，从明天开始数到假期首日，
    /// 因此同一日期白天倒计时显示 N 天、下班时显示 N-1 天。最少显示 1 天，避免出现“还有 0 天”。
    /// </summary>
    private static int DaysAfterToday(HolidayMoment moment) => Math.Max(1, moment.Days - 1);
}
