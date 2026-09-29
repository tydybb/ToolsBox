using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.Core.Tests.WorkCountdown;

public sealed class HolidayFunCopyTests
{
    [Fact]
    public void Detect_InsideHoliday_ReturnsDayIndexAndRemainingDays()
    {
        var moment = HolidayFunCopy.Detect(new DateOnly(2026, 10, 3));
        Assert.Equal(HolidayMomentKind.DuringHoliday, moment.Kind);
        Assert.Equal("国庆节", moment.Name);
        Assert.Equal(3, moment.Days);
        Assert.Equal(4, moment.DaysLeft);
    }

    [Fact]
    public void Detect_LastDayOfHoliday_ReturnsZeroBalance()
    {
        var moment = HolidayFunCopy.Detect(new DateOnly(2026, 10, 7));
        Assert.Equal(HolidayMomentKind.DuringHoliday, moment.Kind);
        Assert.Equal(0, moment.DaysLeft);
    }

    [Fact]
    public void Detect_DayBeforeHolidayStart_IsHolidayEveOnWorkday()
    {
        var moment = HolidayFunCopy.Detect(new DateOnly(2026, 9, 24));
        Assert.Equal(HolidayMomentKind.HolidayEve, moment.Kind);
        Assert.Equal("中秋节", moment.Name);
    }

    [Fact]
    public void Detect_MakeupWorkday_ReportsNextHolidayAndDistance()
    {
        var moment = HolidayFunCopy.Detect(new DateOnly(2026, 9, 20));
        Assert.Equal(HolidayMomentKind.MakeupWorkday, moment.Kind);
        Assert.Equal("中秋节", moment.Name);
        Assert.Equal(5, moment.Days);
    }

    [Fact]
    public void Detect_MakeupWorkdayWithoutLaterHoliday_UsesEmptyName()
    {
        var moment = HolidayFunCopy.Detect(new DateOnly(2026, 10, 10));
        Assert.Equal(HolidayMomentKind.MakeupWorkday, moment.Kind);
        Assert.Equal("", moment.Name);
        Assert.Equal(0, moment.Days);
    }

    [Fact]
    public void Detect_FirstWorkdayAfterHoliday_CombinesWithNextHoliday()
    {
        var moment = HolidayFunCopy.Detect(new DateOnly(2026, 9, 28));
        Assert.Equal(HolidayMomentKind.AfterHoliday, moment.Kind);
        Assert.Equal("中秋节", moment.Name);
        Assert.Equal("国庆节", moment.NextName);
        Assert.Equal(3, moment.Days);
    }

    [Fact]
    public void Detect_NearHoliday_CountsDownWithinSevenDays()
    {
        var moment = HolidayFunCopy.Detect(new DateOnly(2026, 9, 18));
        Assert.Equal(HolidayMomentKind.NearHoliday, moment.Kind);
        Assert.Equal("中秋节", moment.Name);
        Assert.Equal(7, moment.Days);
    }

    [Fact]
    public void Detect_HolidayEightDaysAway_ShowsNothing()
    {
        var moment = HolidayFunCopy.Detect(new DateOnly(2026, 9, 17));
        Assert.Equal(HolidayMomentKind.None, moment.Kind);
        Assert.Equal("", HolidayFunCopy.Countdown(moment));
        Assert.Equal("", HolidayFunCopy.OffWork(moment));
    }

    [Fact]
    public void Detect_DateWithoutHolidayData_ReturnsNoneInsteadOfGuessing()
    {
        var moment = HolidayFunCopy.Detect(new DateOnly(2027, 9, 24));
        Assert.Equal(HolidayMomentKind.None, moment.Kind);
        Assert.Equal("", HolidayFunCopy.Countdown(moment));
        Assert.Equal("", HolidayFunCopy.OffWork(moment));
    }

    [Fact]
    public void Detect_YearEndWithoutUpcomingHoliday_ReturnsNone()
    {
        var moment = HolidayFunCopy.Detect(new DateOnly(2026, 11, 15));
        Assert.Equal(HolidayMomentKind.None, moment.Kind);
    }

    [Theory]
    [InlineData(HolidayMomentKind.DuringHoliday, "假期第")]
    [InlineData(HolidayMomentKind.HolidayEve, "节前最后一个工作日")]
    [InlineData(HolidayMomentKind.MakeupWorkday, "调休补班日")]
    [InlineData(HolidayMomentKind.AfterHoliday, "余额已清零")]
    [InlineData(HolidayMomentKind.NearHoliday, "距离中秋节假期还有")]
    public void Countdown_UsesCountdownVoiceForEveryMoment(HolidayMomentKind kind, string expected)
    {
        var moment = kind switch
        {
            HolidayMomentKind.DuringHoliday => new HolidayMoment(kind, "中秋节", 2, 1, ""),
            HolidayMomentKind.HolidayEve => new HolidayMoment(kind, "中秋节", 0, 0, ""),
            HolidayMomentKind.MakeupWorkday => new HolidayMoment(kind, "中秋节", 5, 0, ""),
            HolidayMomentKind.AfterHoliday => new HolidayMoment(kind, "中秋节", 3, 0, "国庆节"),
            _ => new HolidayMoment(kind, "中秋节", 7, 0, "")
        };
        Assert.Contains(expected, HolidayFunCopy.Countdown(moment));
    }

    [Fact]
    public void OffWork_UsesFinishVoiceWithHolidayName()
    {
        var eve = new HolidayMoment(HolidayMomentKind.HolidayEve, "中秋节", 0, 0, "");
        Assert.Equal("下班即放假，中秋节假期我来啦！", HolidayFunCopy.OffWork(eve));

        var during = new HolidayMoment(HolidayMomentKind.DuringHoliday, "国庆节", 3, 4, "");
        Assert.Contains("第 3 天收工", HolidayFunCopy.OffWork(during));
        Assert.Contains("余额还剩 4 天", HolidayFunCopy.OffWork(during));

        var makeup = new HolidayMoment(HolidayMomentKind.MakeupWorkday, "", 0, 0, "");
        Assert.Equal("补班日准点收工，这班补得值！", HolidayFunCopy.OffWork(makeup));
    }

    [Fact]
    public void OffWork_NearHoliday_ExcludesTodayFromRemainingDays()
    {
        // 2026-09-18 距中秋 7 天：白天倒计时按日历差显示 7 天，收工时今天已过完，应显示 6 天。
        var moment = HolidayFunCopy.Detect(new DateOnly(2026, 9, 18));
        Assert.Equal(HolidayMomentKind.NearHoliday, moment.Kind);
        Assert.Equal(7, moment.Days);
        Assert.Equal("距离中秋节假期还有 7 天，坚持住，放假在望！", HolidayFunCopy.Countdown(moment));
        Assert.Equal("收工！距离中秋节假期还有 6 天，假期在望", HolidayFunCopy.OffWork(moment));
    }

    [Fact]
    public void OffWork_AfterHoliday_ExcludesTodayFromNextHolidayCount()
    {
        // 2026-09-28 距国庆 3 天：收工时应按 9/29、9/30 两天计。
        var moment = HolidayFunCopy.Detect(new DateOnly(2026, 9, 28));
        Assert.Equal(HolidayMomentKind.AfterHoliday, moment.Kind);
        Assert.Equal(3, moment.Days);
        Assert.Equal("收工！中秋节假期余额已清零，国庆节假期还有 2 天到账", HolidayFunCopy.OffWork(moment));
    }

    [Fact]
    public void OffWork_MakeupWorkday_ExcludesTodayFromHolidayCount()
    {
        // 2026-09-20 补班距中秋 5 天：收工时从 9/21 起算 4 天。
        var moment = HolidayFunCopy.Detect(new DateOnly(2026, 9, 20));
        Assert.Equal(HolidayMomentKind.MakeupWorkday, moment.Kind);
        Assert.Equal(5, moment.Days);
        Assert.Equal("补班日准点收工，距离中秋节假期还有 4 天，赚了！", HolidayFunCopy.OffWork(moment));
    }

    [Fact]
    public void OffWork_HolidayStartsTomorrow_StillShowsOneDay()
    {
        var moment = new HolidayMoment(HolidayMomentKind.NearHoliday, "中秋节", 1, 0, "");
        Assert.Equal("收工！距离中秋节假期还有 1 天，假期在望", HolidayFunCopy.OffWork(moment));
    }

    [Fact]
    public void DuringHoliday_ComplainsAboutWorkingThroughHoliday_InsteadOfPraisingIt()
    {
        var moment = new HolidayMoment(HolidayMomentKind.DuringHoliday, "国庆节", 3, 4, "");

        string countdown = HolidayFunCopy.Countdown(moment);
        Assert.Contains("我在加班", countdown);
        Assert.Contains("命苦", countdown);
        Assert.DoesNotContain("致敬", countdown);
        Assert.DoesNotContain("好好休息", countdown);

        string offWork = HolidayFunCopy.OffWork(moment);
        Assert.Contains("我加班", offWork);
        Assert.Contains("命苦", offWork);
        Assert.DoesNotContain("好好休息", offWork);
        Assert.DoesNotContain("假期圆满", offWork);
    }
}
