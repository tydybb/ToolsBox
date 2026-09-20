namespace ToolsBox.Core.WorkCountdown;

public static class CountdownCalculator
{
    private const long Hour = TimeSpan.TicksPerHour;
    private const long WorkingDayTicks = 22 * Hour + Hour / 2;

    public static WorkSchedule Calculate(DateOnly date, TimeOnly startTime, DayKind kind, int overtimeHours)
    {
        if (startTime < new TimeOnly(7, 30))
            throw new ArgumentException("上班时间不能早于 07:30。", nameof(startTime));
        if (kind is not (DayKind.Workday or DayKind.RestDay))
            throw new ArgumentOutOfRangeException(nameof(kind), "请先确认当天是工作日还是休息日。");
        if ((kind == DayKind.Workday && overtimeHours != 0 && overtimeHours < 2)
            || (kind == DayKind.RestDay && overtimeHours < 4))
            throw new ArgumentOutOfRangeException(nameof(overtimeHours), "工作日加班至少 2 小时，休息日加班至少 4 小时。");

        var start = date.ToDateTime(startTime);
        var isLate = kind == DayKind.Workday && startTime > new TimeOnly(9, 30);
        DateTime? normalEnd = kind == DayKind.Workday
            ? isLate ? date.ToDateTime(new TimeOnly(18, 30)) : start.AddHours(9)
            : null;
        var overtimeStart = normalEnd ?? start;
        if (overtimeHours == 0)
            return new WorkSchedule(start, normalEnd, overtimeStart, isLate, TimeSpan.Zero);

        // Remove the daily breaks from the time axis. This permits any representable
        // duration in constant time, without iterating through hours or calendar days.
        var workingStart = ToWorkingTicks(overtimeStart);
        var available = ToWorkingTicks(DateTime.MaxValue) - workingStart;
        if (overtimeHours > available / Hour)
            throw new ArgumentOutOfRangeException(nameof(overtimeHours), "加班时长超出可表示的日期范围。");

        var requestedTicks = (long)overtimeHours * Hour;
        var end = FromWorkingTicks(workingStart + requestedTicks);
        var excluded = end - overtimeStart - TimeSpan.FromTicks(requestedTicks);
        return new WorkSchedule(start, normalEnd, end, isLate, excluded);
    }

    private static long ToWorkingTicks(DateTime time)
    {
        var dayTicks = time.TimeOfDay.Ticks;
        var lunch = Math.Clamp(dayTicks - 12 * Hour, 0, Hour);
        var dinner = Math.Clamp(dayTicks - 19 * Hour, 0, Hour / 2);
        return time.Ticks / TimeSpan.TicksPerDay * WorkingDayTicks + dayTicks - lunch - dinner;
    }

    private static DateTime FromWorkingTicks(long workingTicks)
    {
        var day = workingTicks / WorkingDayTicks;
        var dayTicks = workingTicks % WorkingDayTicks;
        // Strict comparisons keep an end exactly at 12:00 or 19:00 at the
        // start of the break; only work beyond that boundary incurs the break.
        var breaks = dayTicks > 18 * Hour ? Hour + Hour / 2 : dayTicks > 12 * Hour ? Hour : 0;
        return new DateTime(day * TimeSpan.TicksPerDay + dayTicks + breaks, DateTimeKind.Unspecified);
    }
}
