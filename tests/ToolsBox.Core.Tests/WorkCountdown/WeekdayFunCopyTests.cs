using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.Core.Tests.WorkCountdown;

public sealed class WeekdayFunCopyTests
{
    [Theory]
    [InlineData(2026, 11, 9, "周一")]
    [InlineData(2026, 11, 10, "周二")]
    [InlineData(2026, 11, 11, "周三")]
    [InlineData(2026, 11, 12, "周四")]
    [InlineData(2026, 11, 13, "周五")]
    [InlineData(2026, 11, 14, "周六")]
    [InlineData(2026, 11, 15, "周日")]
    public void Countdown_CoversEveryWeekday_WithMatchingDayName(int year, int month, int day, string weekday)
    {
        var date = new DateOnly(year, month, day);
        string copy = WeekdayFunCopy.Countdown(date);
        Assert.False(string.IsNullOrWhiteSpace(copy));
        Assert.Contains(weekday, copy);
    }

    [Theory]
    [InlineData(2026, 11, 9, "周一")]
    [InlineData(2026, 11, 13, "周五")]
    [InlineData(2026, 11, 15, "周日")]
    public void OffWork_CoversEveryWeekday_WithMatchingDayName(int year, int month, int day, string weekday)
    {
        var date = new DateOnly(year, month, day);
        string copy = WeekdayFunCopy.OffWork(date);
        Assert.False(string.IsNullOrWhiteSpace(copy));
        Assert.Contains(weekday, copy);
    }

    [Fact]
    public void Countdown_RotatesAcrossWeeks_ButStaysStableWithinADay()
    {
        var week1 = new DateOnly(2026, 11, 13);
        var week2 = week1.AddDays(7);
        var sameDay = week1.AddDays(0);
        Assert.Equal(WeekdayFunCopy.Countdown(week1), WeekdayFunCopy.Countdown(sameDay));
        Assert.NotEqual(WeekdayFunCopy.Countdown(week1), WeekdayFunCopy.Countdown(week2));
    }

    [Fact]
    public void OffWork_DiffersFromCountdownVoice_ForTheSameDate()
    {
        var date = new DateOnly(2026, 11, 13);
        Assert.NotEqual(WeekdayFunCopy.Countdown(date), WeekdayFunCopy.OffWork(date));
    }

    [Fact]
    public void Countdown_WorksForYearsWithoutHolidayCalendar()
    {
        string copy = WeekdayFunCopy.Countdown(new DateOnly(2027, 9, 24));
        Assert.Contains("周五", copy);
    }
}
