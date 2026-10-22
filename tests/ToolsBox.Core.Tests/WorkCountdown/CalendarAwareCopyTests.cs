using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.Core.Tests.WorkCountdown;

public sealed class CalendarAwareCopyTests
{
    [Theory]
    [InlineData(10, 9, "周五", "明天调休补班")]
    [InlineData(10, 10, "周六", "明天休息")]
    [InlineData(11, 13, "周五", "明天休息")]
    [InlineData(9, 24, "周四", "明天休息")]
    [InlineData(10, 7, "周三", "明天上班")]
    [InlineData(1, 3, "周六", "明天调休补班")]
    public void WeekdayCopy_UsesTheKnownNextDayRatherThanJustItsWeekday(
        int month, int day, string weekday, string tomorrow)
    {
        DateOnly date = new(2026, month, day);

        foreach (string copy in new[] { WeekdayFunCopy.Countdown(date), WeekdayFunCopy.OffWork(date) })
        {
            Assert.Contains(weekday, copy);
            Assert.Contains(tomorrow, copy);
            Assert.DoesNotContain("48 小时", copy);
        }
    }

    [Fact]
    public void MakeupSaturday_DoesNotPromiseAFreeWeekend()
    {
        DateOnly date = new(2026, 10, 10);

        foreach (string copy in new[] { WeekdayFunCopy.Countdown(date), WeekdayFunCopy.OffWork(date) })
        {
            Assert.Contains("调休补班", copy);
            Assert.DoesNotContain("自然醒", copy);
            Assert.DoesNotContain("自由时间继续", copy);
        }
    }

    [Fact]
    public void ThursdayBeforeAnExtendedWorkWeek_DoesNotClaimToBeThePenultimateWorkday()
    {
        DateOnly date = new(2026, 2, 12);
        Assert.DoesNotContain("倒数第二", WeekdayFunCopy.Countdown(date));
        Assert.DoesNotContain("周末已经", WeekdayFunCopy.OffWork(date));
    }

    [Theory]
    [InlineData(2027, 9, 24)]
    [InlineData(2026, 12, 31)]
    public void MissingTomorrowCalendar_DoesNotPromiseTimeOff(int year, int month, int day)
    {
        DateOnly date = new(year, month, day);

        foreach (string copy in new[] { WeekdayFunCopy.Countdown(date), WeekdayFunCopy.OffWork(date) })
        {
            Assert.DoesNotContain("48 小时", copy);
            Assert.DoesNotContain("周末模式", copy);
            Assert.DoesNotContain("明天休息", copy);
            Assert.DoesNotContain("明天放假", copy);
        }
    }

    [Theory]
    [InlineData(10, 9, "明天调休补班", "xiaohe")]
    [InlineData(10, 10, "明天休息", "tianzhong")]
    [InlineData(11, 13, "明天休息", "xiaohe")]
    [InlineData(9, 24, "明天休息", "tianzhong")]
    public void ReactionCaption_KeepsCalendarContextWhilePreservingItsCharacter(
        int month, int day, string tomorrow, string character)
    {
        DateOnly date = new(2026, month, day);
        WorkSchedule schedule = CountdownCalculator.Calculate(date, new TimeOnly(8, 30), DayKind.Workday, 0);
        var context = new CountdownReactionContext(date, DayKind.Workday, schedule, schedule.Start, null, 0);

        CountdownReaction working = CountdownReactionSelector.Select(context);
        CountdownReaction finished = CountdownReactionSelector.Select(context with
        {
            Now = schedule.End,
            FinishedAt = schedule.End
        });

        Assert.Equal($"{character}/calm", working.AnimationKey);
        Assert.Equal($"{character}/happy", finished.AnimationKey);
        Assert.Contains(tomorrow, working.Caption);
        Assert.Contains(tomorrow, finished.Caption);
    }

    [Theory]
    [InlineData(10, 7, "明天上班")]
    [InlineData(1, 3, "明天调休补班")]
    public void RestDayReaction_StillReportsTheNextWorkday(int month, int day, string tomorrow)
    {
        DateOnly date = new(2026, month, day);
        var context = new CountdownReactionContext(date, DayKind.RestDay, null, date.ToDateTime(new(10, 0)), null, 0);

        CountdownReaction reaction = CountdownReactionSelector.Select(context);

        Assert.Equal("xiuzhen/happy", reaction.AnimationKey);
        Assert.Contains(tomorrow, reaction.Caption);
    }

    [Fact]
    public void MakeupHolidayCopy_DoesNotInventUpcomingLeaveOrAnOnTimeFinish()
    {
        HolidayMoment moment = HolidayFunCopy.Detect(new DateOnly(2026, 10, 10));

        Assert.Contains("调休补班", HolidayFunCopy.Countdown(moment));
        Assert.DoesNotContain("为长假充值", HolidayFunCopy.Countdown(moment));
        Assert.Contains("补班", HolidayFunCopy.OffWork(moment));
        Assert.DoesNotContain("准点", HolidayFunCopy.OffWork(moment));
    }

    [Fact]
    public void ManualRestDayOverride_ChangesOnlyTodayAndKeepsTomorrowsMakeupWorkday()
    {
        DateOnly date = new(2026, 10, 9);

        Assert.Contains("今天休息", WeekdayFunCopy.Countdown(date, DayKind.RestDay));
        Assert.Contains("明天调休补班", WeekdayFunCopy.Countdown(date, DayKind.RestDay));
        Assert.Contains("明天调休补班", WeekdayFunCopy.OffWork(date, DayKind.RestDay));
        Assert.DoesNotContain("周末模式", WeekdayFunCopy.OffWork(date, DayKind.RestDay));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(28)]
    public void ManualWorkdayOverride_OnAWeekendUsesTheWorkingVoice(int day)
    {
        DateOnly date = new(2026, 11, day);

        Assert.Contains("周末上班", WeekdayFunCopy.Countdown(date, DayKind.Workday));
        Assert.DoesNotContain("今天休息", WeekdayFunCopy.Countdown(date, DayKind.Workday));
        Assert.DoesNotContain("自然醒", WeekdayFunCopy.Countdown(date, DayKind.Workday));
        Assert.DoesNotContain("日历排了班", WeekdayFunCopy.Countdown(date, DayKind.Workday));
    }

    [Fact]
    public void ManualRestDayOverride_OnMakeupSaturdayDoesNotDescribeTodayAsWorking()
    {
        DateOnly date = new(2026, 10, 10);

        Assert.Contains("今天休息", WeekdayFunCopy.Countdown(date, DayKind.RestDay));
        Assert.DoesNotContain("调休补班", WeekdayFunCopy.Countdown(date, DayKind.RestDay));
        Assert.Contains("明天休息", WeekdayFunCopy.Countdown(date, DayKind.RestDay));
    }

    [Theory]
    [InlineData(10, 10, "明天休息")]
    [InlineData(10, 7, "明天上班")]
    [InlineData(1, 3, "明天调休补班")]
    public void MakeupAndLastHolidayDayCopy_ReportsKnownTomorrowWhenDateIsProvided(
        int month, int day, string tomorrow)
    {
        DateOnly date = new(2026, month, day);
        HolidayMoment moment = HolidayFunCopy.Detect(date);

        Assert.Contains(tomorrow, HolidayFunCopy.Countdown(moment, date));
        Assert.Contains(tomorrow, HolidayFunCopy.OffWork(moment, date));
    }

    [Fact]
    public void MaxDate_HasNeutralCopyWithoutTryingToReadTheNextDay()
    {
        Assert.Equal("", WeekdayFunCopy.TomorrowDescription(DateOnly.MaxValue));
        Assert.DoesNotContain("明天", WeekdayFunCopy.Countdown(DateOnly.MaxValue));
        Assert.DoesNotContain("明天", WeekdayFunCopy.OffWork(DateOnly.MaxValue));
    }

    [Theory]
    [InlineData(13)]
    [InlineData(20)]
    [InlineData(27)]
    public void UnknownYearSaturday_DoesNotClaimThatTheCalendarHasScheduledWork(int day)
    {
        DateOnly date = new(2027, 11, day);

        string copy = WeekdayFunCopy.Countdown(date);

        Assert.Contains("日期类型待确认", copy);
        Assert.DoesNotContain("日历排了班", copy);
    }

    [Theory]
    [InlineData(0, 30, "加班已经")]
    [InlineData(8, 0, "无偿加班")]
    public void CrossMidnightReaction_LabelsItsTaskDateButUsesTheActualNextCalendarDay(
        int hour, int minute, string stage)
    {
        DateOnly date = new(2026, 10, 9);
        WorkSchedule schedule = CountdownCalculator.Calculate(date, new TimeOnly(8, 30), DayKind.Workday, 8);
        DateTime now = new(2026, 10, 10, hour, minute, 0);

        CountdownReaction reaction = CountdownReactionSelector.Select(new(date, DayKind.Workday, schedule, now, null, 8));

        Assert.Equal("tianzhong/stressed", reaction.AnimationKey);
        Assert.Contains("2026-10-09", reaction.Caption);
        Assert.Contains("周五", reaction.Caption);
        Assert.Contains("明天休息", reaction.Caption);
        Assert.Contains(stage, reaction.Caption);
        Assert.DoesNotContain("明天调休补班", reaction.Caption);
    }

    [Fact]
    public void CrossMidnightFinishedReaction_CapturesTheFinishDatesTomorrowAndFreezes()
    {
        DateOnly date = new(2026, 10, 9);
        WorkSchedule schedule = CountdownCalculator.Calculate(date, new TimeOnly(8, 30), DayKind.Workday, 8);
        DateTime finished = new(2026, 10, 10, 2, 0, 0);
        var context = new CountdownReactionContext(date, DayKind.Workday, schedule, finished, finished, 8);

        CountdownReaction reaction = CountdownReactionSelector.Select(context);
        CountdownReaction later = CountdownReactionSelector.Select(context with { Now = finished.AddDays(7) });

        Assert.Equal("xiaohe/happy", reaction.AnimationKey);
        Assert.Contains("2026-10-09", reaction.Caption);
        Assert.Contains("明天休息", reaction.Caption);
        Assert.DoesNotContain("明天调休补班", reaction.Caption);
        Assert.Equal(reaction, later);
    }

    [Fact]
    public void OneArgumentCopyMethods_KeepTheirDelegateContracts()
    {
        Func<DateOnly, string> weekdayCountdown = WeekdayFunCopy.Countdown;
        Func<DateOnly, string> weekdayOffWork = WeekdayFunCopy.OffWork;
        Func<HolidayMoment, string> holidayCountdown = HolidayFunCopy.Countdown;
        Func<HolidayMoment, string> holidayOffWork = HolidayFunCopy.OffWork;
        DateOnly date = new(2026, 10, 9);
        HolidayMoment moment = HolidayFunCopy.Detect(new(2026, 10, 10));

        Assert.Equal(WeekdayFunCopy.Countdown(date), weekdayCountdown(date));
        Assert.Equal(WeekdayFunCopy.OffWork(date), weekdayOffWork(date));
        Assert.Equal(HolidayFunCopy.Countdown(moment), holidayCountdown(moment));
        Assert.Equal(HolidayFunCopy.OffWork(moment), holidayOffWork(moment));
    }
}
