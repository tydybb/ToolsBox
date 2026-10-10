namespace ToolsBox.Core.WorkCountdown;

public sealed record CountdownReactionContext(
    DateOnly WorkDate,
    DayKind Kind,
    WorkSchedule? Schedule,
    DateTime Now,
    DateTime? FinishedAt,
    int OvertimeHours);

public sealed record CountdownReaction(string AnimationKey, string CharacterName, string Caption);

public static class CountdownReactionSelector
{
    public static CountdownReaction Select(CountdownReactionContext context)
    {
        string character = BaseCharacter(context.WorkDate, context.Kind);
        DateOnly referenceDate = DateOnly.FromDateTime(context.FinishedAt ?? context.Now);
        string day = WeekdayFunCopy.DayDescription(context.WorkDate, context.Kind, context.Schedule is not null, referenceDate);
        WorkSchedule? schedule = context.Schedule;

        if (context.FinishedAt is DateTime finished)
        {
            bool early = schedule?.NormalEnd is DateTime normalEnd && finished < normalEnd;
            return Reaction(character, "happy", early
                ? $"{day}，提前收工，{ShortName(character)}陪你把脚步切到回家频道。"
                : $"{day}，下班啦！{ShortName(character)}陪你把工作留在今天。");
        }

        if (schedule is not null && context.Now >= schedule.End)
            return Reaction("tianzhong", "stressed", $"{day}，计划时间都过了，田中替你盯着这段无偿加班。");

        if (schedule is not null && context.OvertimeHours >= (context.Kind == DayKind.RestDay ? 22 : 15))
            return Reaction("tianzhong", "stressed", $"{day}，这份加班计划长得离谱，田中建议先休息，别把自己熬成传说。");

        if (schedule is null)
            return context.Kind == DayKind.RestDay
                ? Reaction(character, "happy", $"{day}，秀珍把今天留给散步和小快乐。")
                : Reaction(character, "calm", $"{day}，{ShortName(character)}已就位，等你开启今天的倒计时。");

        if (context.Now < schedule.Start)
            return Reaction(character, "calm", $"{day}，{ShortName(character)}先眨眨眼，等到点开工。");

        DateTime overtimeStart = schedule.NormalEnd ?? schedule.Start;
        if (context.OvertimeHours > 0 && context.Now >= overtimeStart)
        {
            double overtimeElapsed = (context.Now - overtimeStart).TotalHours;
            if (overtimeElapsed >= 5)
                return Reaction("tianzhong", "stressed", $"{day}，加班已经续了好几集，田中提醒你歇一歇，身体也要下班。");
            if (overtimeElapsed >= 2)
                return Reaction("xiaohe", "tired", $"{day}，加班还在继续，小河的眼皮快撑不住了，忙完记得回家。");
            return Reaction("duodong", "tired", $"{day}，加班副本开了，多栋陪你忙完这段就回家。");
        }

        if (schedule.End - context.Now <= TimeSpan.FromMinutes(30))
            return Reaction(character, "happy", $"{day}，快乐已经排到门口，{ShortName(character)}准备接你下班。");

        double elapsedHours = (context.Now - schedule.Start).TotalHours;
        if (elapsedHours >= 5)
            return Reaction("xiaohe", "tired", $"{day}，已经忙了大半天，小河替你把下班盼近一点。");
        if (elapsedHours >= 2)
            return Reaction(character, "tired", $"{day}，{ShortName(character)}的电量开始见底，喝口水再继续。");
        return Reaction(character, "calm", $"{day}，{ShortName(character)}陪你慢慢进入状态，先把今天开个好头。");
    }

    private static string BaseCharacter(DateOnly date, DayKind kind)
    {
        if (kind == DayKind.RestDay) return "xiuzhen";
        if (kind == DayKind.Workday && (ChinaWorkCalendar.IsMakeupWorkday(date)
            || ChinaWorkCalendar.GetHolidayAt(date) is not null
            || date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)) return "tianzhong";
        return date.DayOfWeek switch
        {
            DayOfWeek.Monday => "lipu",
            DayOfWeek.Tuesday => "duodong",
            DayOfWeek.Wednesday => "xiuzhen",
            DayOfWeek.Thursday => "tianzhong",
            DayOfWeek.Friday => "xiaohe",
            _ => "xiuzhen"
        };
    }

    private static CountdownReaction Reaction(string character, string mood, string caption) =>
        new($"{character}/{mood}", character switch
        {
            "lipu" => "厘普",
            "duodong" => "多栋",
            "xiuzhen" => "秀珍",
            "tianzhong" => "田中",
            _ => "小河"
        }, caption);

    private static string ShortName(string character) => character switch
    {
        "lipu" => "厘普",
        "duodong" => "多栋",
        "xiuzhen" => "秀珍",
        "tianzhong" => "田中",
        _ => "小河"
    };
}
