using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.Core.Tests.WorkCountdown;

public sealed class CountdownReactionTests
{
    [Theory]
    [InlineData(9, "lipu/calm", "厘普")]
    [InlineData(10, "duodong/calm", "多栋")]
    [InlineData(11, "xiuzhen/calm", "秀珍")]
    [InlineData(12, "tianzhong/calm", "田中")]
    [InlineData(13, "xiaohe/calm", "小河")]
    public void OrdinaryWeekday_SelectsItsBaseCharacter(int day, string key, string name)
    {
        DateOnly date = new(2026, 11, day);
        WorkSchedule schedule = Schedule(date);

        CountdownReaction reaction = Select(date, schedule, schedule.Start);

        Assert.Equal(key, reaction.AnimationKey);
        Assert.Equal(name, reaction.CharacterName);
        Assert.NotEmpty(reaction.Caption);
    }

    [Theory]
    [InlineData(1, 59, 59, "lipu/calm")]
    [InlineData(2, 0, 0, "lipu/tired")]
    [InlineData(4, 59, 59, "lipu/tired")]
    [InlineData(5, 0, 0, "xiaohe/tired")]
    public void WorkingStage_UsesElapsedHoursAtExactBoundaries(int hours, int minutes, int seconds, string key)
    {
        DateOnly date = new(2026, 11, 9);
        WorkSchedule schedule = Schedule(date);

        CountdownReaction reaction = Select(date, schedule, schedule.Start.AddHours(hours).AddMinutes(minutes).AddSeconds(seconds));

        Assert.Equal(key, reaction.AnimationKey);
    }

    [Fact]
    public void WorkingStage_DoesNotDeductBreaksFromRealElapsedTime()
    {
        DateOnly date = new(2026, 11, 9);
        WorkSchedule schedule = Schedule(date) with { ExcludedBreaks = TimeSpan.FromHours(3) };

        Assert.Equal("xiaohe/tired", Select(date, schedule, schedule.Start.AddHours(5)).AnimationKey);
    }

    [Theory]
    [InlineData(30, 1, "xiaohe/tired")]
    [InlineData(30, 0, "lipu/happy")]
    [InlineData(0, 1, "lipu/happy")]
    [InlineData(0, 0, "tianzhong/stressed")]
    public void ApproachingEnd_HasAnInclusiveThirtyMinuteBoundary(int minutes, int seconds, string key)
    {
        DateOnly date = new(2026, 11, 9);
        WorkSchedule schedule = Schedule(date);

        Assert.Equal(key, Select(date, schedule, schedule.End.AddMinutes(-minutes).AddSeconds(-seconds)).AnimationKey);
    }

    [Fact]
    public void HolidayRestDayWithoutTask_IsRelaxed()
    {
        DateOnly date = new(2026, 10, 3);
        CountdownReaction reaction = CountdownReactionSelector.Select(new(date, DayKind.RestDay, null, date.ToDateTime(new(10, 0)), null, 0));

        Assert.Equal("xiuzhen/happy", reaction.AnimationKey);
        Assert.Contains("假期", reaction.Caption);
    }

    [Fact]
    public void MakeupWorkday_IsNotWeekendRelaxation()
    {
        DateOnly date = new(2026, 10, 10);
        WorkSchedule schedule = Schedule(date);
        CountdownReaction reaction = Select(date, schedule, schedule.Start);

        Assert.Equal("tianzhong/calm", reaction.AnimationKey);
        Assert.Contains("补班", reaction.Caption);
    }

    [Theory]
    [InlineData(10, 3, "假期上班")]
    [InlineData(11, 14, "周末上班")]
    public void ManualWorkdayOverride_UsesWorkingVoiceOnCalendarRestDays(int month, int day, string caption)
    {
        DateOnly date = new(2026, month, day);
        WorkSchedule schedule = Schedule(date);
        CountdownReaction reaction = Select(date, schedule, schedule.Start);

        Assert.Equal("tianzhong/calm", reaction.AnimationKey);
        Assert.Contains(caption, reaction.Caption);
        Assert.DoesNotContain("闲逛", reaction.Caption);
    }

    [Fact]
    public void ManualRestDayOverride_DoesNotUseMakeupWorkdayVoice()
    {
        DateOnly date = new(2026, 10, 10);
        CountdownReaction reaction = CountdownReactionSelector.Select(new(date, DayKind.RestDay, null, date.ToDateTime(new(10, 0)), null, 0));

        Assert.Equal("xiuzhen/happy", reaction.AnimationKey);
        Assert.DoesNotContain("补班", reaction.Caption);
    }

    [Fact]
    public void FutureStart_RemainsCalmEvenCloseToItsEnd()
    {
        DateOnly date = new(2026, 11, 9);
        DateTime start = date.ToDateTime(new(9, 0));
        WorkSchedule schedule = new(start, start.AddMinutes(10), start.AddMinutes(10), false, TimeSpan.Zero);

        CountdownReaction reaction = Select(date, schedule, start.AddSeconds(-1));

        Assert.Equal("lipu/calm", reaction.AnimationKey);
        Assert.Contains("开工", reaction.Caption);
    }

    [Theory]
    [InlineData(-1, "xiaohe/tired")]
    [InlineData(0, "duodong/tired")]
    public void PlannedWorkdayOvertime_StartsAtNormalEnd(int secondsFromNormalEnd, string key)
    {
        DateOnly date = new(2026, 11, 13);
        WorkSchedule schedule = Schedule(date, 4);
        DateTime now = schedule.NormalEnd!.Value.AddSeconds(secondsFromNormalEnd);

        CountdownReaction reaction = Select(date, schedule, now, overtime: 4);

        Assert.Equal(key, reaction.AnimationKey);
    }

    [Theory]
    [InlineData(0, "duodong/tired")]
    [InlineData(2, "xiaohe/tired")]
    [InlineData(5, "tianzhong/stressed")]
    public void PlannedOvertime_ChangesCharacterAsItsElapsedTimeIncreases(int elapsedHours, string key)
    {
        DateOnly date = new(2026, 11, 13);
        WorkSchedule schedule = Schedule(date, 8);

        CountdownReaction reaction = Select(date, schedule, schedule.NormalEnd!.Value.AddHours(elapsedHours), overtime: 8);

        Assert.Equal(key, reaction.AnimationKey);
        Assert.Contains("加班", reaction.Caption);
    }

    [Fact]
    public void RestDayOvertime_StartsAtClockInAndWinsOverNearEnd()
    {
        DateOnly date = new(2026, 11, 14);
        WorkSchedule schedule = Schedule(date, 4, DayKind.RestDay);

        Assert.Equal("duodong/tired", Select(date, schedule, schedule.Start, DayKind.RestDay, 4).AnimationKey);
        Assert.Equal("xiaohe/tired", Select(date, schedule, schedule.End.AddMinutes(-1), DayKind.RestDay, 4).AnimationKey);
    }

    [Theory]
    [InlineData(DayKind.Workday, 14, "lipu/calm")]
    [InlineData(DayKind.Workday, 15, "tianzhong/stressed")]
    [InlineData(DayKind.RestDay, 21, "xiuzhen/calm")]
    [InlineData(DayKind.RestDay, 22, "tianzhong/stressed")]
    public void UnrealisticOvertime_UsesKindSpecificThresholdEvenBeforeStart(DayKind kind, int hours, string key)
    {
        DateOnly date = new(2026, 11, 9);
        WorkSchedule schedule = Schedule(date, hours, kind);

        CountdownReaction reaction = Select(date, schedule, schedule.Start.AddMinutes(-1), kind, hours);

        Assert.Equal(key, reaction.AnimationKey);
        if (key.EndsWith("stressed")) Assert.Contains("休息", reaction.Caption);
    }

    [Fact]
    public void UnpaidOvertime_WinsOverUnrealisticPlanAndFridayExcitement()
    {
        DateOnly date = new(2026, 11, 13);
        WorkSchedule schedule = Schedule(date, 15);

        CountdownReaction reaction = Select(date, schedule, schedule.End, overtime: 15);

        Assert.Equal("tianzhong/stressed", reaction.AnimationKey);
        Assert.Contains("无偿加班", reaction.Caption);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinishedReaction_WinsOverUnpaidAndUnrealisticOvertimeAndFreezes(bool early)
    {
        DateOnly date = new(2026, 11, 13);
        WorkSchedule schedule = Schedule(date, 15);
        DateTime finished = early ? schedule.NormalEnd!.Value.AddMinutes(-1) : schedule.End.AddMinutes(1);
        var context = new CountdownReactionContext(date, DayKind.Workday, schedule, finished, finished, 15);

        CountdownReaction reaction = CountdownReactionSelector.Select(context);
        CountdownReaction later = CountdownReactionSelector.Select(context with { Now = finished.AddDays(7) });

        Assert.EndsWith("/happy", reaction.AnimationKey);
        Assert.Equal(reaction, later);
        Assert.Contains(early ? "提前收工" : "下班", reaction.Caption);
        Assert.DoesNotContain("无偿加班", reaction.Caption);
    }

    [Fact]
    public void CrossMidnight_KeepsTheTaskDateRatherThanTheCurrentHoliday()
    {
        DateOnly date = new(2026, 9, 24);
        WorkSchedule schedule = Schedule(date, 8);
        DateTime now = new(2026, 9, 25, 0, 30, 0);

        CountdownReaction reaction = Select(date, schedule, now, overtime: 8);
        CountdownReaction followingSecond = Select(date, schedule, now.AddSeconds(1), overtime: 8);

        Assert.Equal("tianzhong/stressed", reaction.AnimationKey);
        Assert.Equal(reaction, followingSecond);
        Assert.Contains("周四", reaction.Caption);
        Assert.Contains("明天休息（中秋节假期）", reaction.Caption);
        Assert.DoesNotContain("假期上班", reaction.Caption);
    }

    [Fact]
    public void MissingCalendarYear_StillUsesWeekdayAndManualKind()
    {
        DateOnly date = new(2027, 11, 8);
        WorkSchedule schedule = Schedule(date);

        Assert.Equal("lipu/calm", Select(date, schedule, schedule.Start).AnimationKey);
    }

    private static WorkSchedule Schedule(DateOnly date, int overtime = 0, DayKind kind = DayKind.Workday) =>
        CountdownCalculator.Calculate(date, new TimeOnly(8, 30), kind, overtime);

    private static CountdownReaction Select(DateOnly date, WorkSchedule schedule, DateTime now,
        DayKind kind = DayKind.Workday, int overtime = 0) =>
        CountdownReactionSelector.Select(new(date, kind, schedule, now, null, overtime));
}
