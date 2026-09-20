using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.Core.Tests.WorkCountdown;

public sealed class ChinaWorkCalendarTests
{
    [Theory]
    [InlineData(1, 1, 3, "元旦")]
    [InlineData(2, 15, 23, "春节")]
    [InlineData(4, 4, 6, "清明")]
    [InlineData(5, 1, 5, "劳动")]
    [InlineData(6, 19, 21, "端午")]
    [InlineData(9, 25, 27, "中秋")]
    [InlineData(10, 1, 7, "国庆")]
    public void GetDay_RecognizesEveryOfficialHolidayDate(int month, int first, int last, string name)
    {
        for (var day = first; day <= last; day++)
        {
            var result = ChinaWorkCalendar.GetDay(new DateOnly(2026, month, day));
            Assert.Equal(DayKind.RestDay, result.Kind);
            Assert.Contains(name, result.Label);
        }
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 14)]
    [InlineData(2, 28)]
    [InlineData(5, 9)]
    [InlineData(9, 20)]
    [InlineData(10, 10)]
    public void GetDay_MakeupWorkdayOverridesWeekend(int month, int day)
    {
        var result = ChinaWorkCalendar.GetDay(new DateOnly(2026, month, day));
        Assert.Equal(DayKind.Workday, result.Kind);
        Assert.Contains("调休", result.Label);
    }

    [Fact]
    public void GetDay_CoversAllOtherDatesWithTheirWeekdayClassification()
    {
        HashSet<DateOnly> exceptions = [
            new(2026, 1, 4), new(2026, 2, 14), new(2026, 2, 28),
            new(2026, 5, 9), new(2026, 9, 20), new(2026, 10, 10)];
        (int Month, int First, int Last)[] holidays = [
            (1, 1, 3), (2, 15, 23), (4, 4, 6), (5, 1, 5),
            (6, 19, 21), (9, 25, 27), (10, 1, 7)];
        foreach (var (month, first, last) in holidays)
            for (var day = first; day <= last; day++)
                exceptions.Add(new DateOnly(2026, month, day));

        for (var date = new DateOnly(2026, 1, 1); date.Year == 2026; date = date.AddDays(1))
        {
            if (exceptions.Contains(date))
                continue;
            var expected = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                ? DayKind.RestDay : DayKind.Workday;
            var actual = ChinaWorkCalendar.GetDay(date);
            Assert.Equal(expected, actual.Kind);
            Assert.False(string.IsNullOrWhiteSpace(actual.Label));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2025)]
    [InlineData(2027)]
    [InlineData(9999)]
    public void GetDay_UnknownYearIsNeverGuessedFromWeekday(int year)
    {
        for (var day = 1; day <= 7; day++)
        {
            var result = ChinaWorkCalendar.GetDay(new DateOnly(year, 1, day));
            Assert.Equal(DayKind.Unknown, result.Kind);
            Assert.False(string.IsNullOrWhiteSpace(result.Label));
        }
    }
}
