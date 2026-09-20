using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.Core.Tests.WorkCountdown;

public sealed class CountdownCalculatorTests
{
    private static readonly DateOnly Date = new(2026, 9, 18);

    [Theory]
    [InlineData("07:30", "16:30", false)]
    [InlineData("08:00", "17:00", false)]
    [InlineData("09:30", "18:30", false)]
    [InlineData("09:30:01", "18:30", true)]
    [InlineData("10:00", "18:30", true)]
    public void Workday_UsesNineHoursOrFixedLateEnd(string start, string end, bool late)
    {
        var result = CountdownCalculator.Calculate(Date, TimeOnly.Parse(start), DayKind.Workday, 0);

        Assert.Equal(Date.ToDateTime(TimeOnly.Parse(start)), result.Start);
        Assert.Equal(Date.ToDateTime(TimeOnly.Parse(end)), result.NormalEnd);
        Assert.Equal(result.NormalEnd, result.End);
        Assert.Equal(late, result.IsLate);
        Assert.Equal(TimeSpan.Zero, result.ExcludedBreaks);
    }

    [Theory]
    [InlineData("08:00", 2, "19:00", 0, 0)]
    [InlineData("08:00", 3, "20:30", 0, 30)]
    [InlineData("09:30", 2, "21:00", 0, 30)]
    [InlineData("10:00", 2, "21:00", 0, 30)]
    [InlineData("09:30", 6, "01:00", 1, 30)]
    [InlineData("09:30", 18, "14:00", 1, 90)]
    public void Workday_OnlyExcludesBreaksThatOvertimeOverlaps(
        string start, int hours, string end, int days, int excludedMinutes)
    {
        var result = CountdownCalculator.Calculate(Date, TimeOnly.Parse(start), DayKind.Workday, hours);

        Assert.Equal(Date.AddDays(days).ToDateTime(TimeOnly.Parse(end)), result.End);
        Assert.Equal(TimeSpan.FromMinutes(excludedMinutes), result.ExcludedBreaks);
    }

    [Theory]
    [InlineData("08:00", 4, "12:00", 0, 0)]
    [InlineData("08:00", 5, "14:00", 0, 60)]
    [InlineData("12:00", 4, "17:00", 0, 60)]
    [InlineData("12:30", 4, "17:00", 0, 30)]
    [InlineData("13:00", 4, "17:00", 0, 0)]
    [InlineData("15:00", 4, "19:00", 0, 0)]
    [InlineData("16:00", 4, "20:30", 0, 30)]
    [InlineData("19:00", 4, "23:30", 0, 30)]
    [InlineData("19:15", 4, "23:30", 0, 15)]
    [InlineData("19:30", 4, "23:30", 0, 0)]
    [InlineData("20:00", 4, "00:00", 1, 0)]
    [InlineData("08:00", 24, "09:30", 1, 90)]
    [InlineData("08:00", 48, "11:00", 2, 180)]
    [InlineData("08:00", 49, "12:00", 2, 180)]
    [InlineData("08:00", 50, "14:00", 2, 240)]
    public void RestDay_CountsEffectiveHoursAcrossDailyBreaks(
        string start, int hours, string end, int days, int excludedMinutes)
    {
        var result = CountdownCalculator.Calculate(Date, TimeOnly.Parse(start), DayKind.RestDay, hours);

        Assert.Null(result.NormalEnd);
        Assert.False(result.IsLate);
        Assert.Equal(Date.AddDays(days).ToDateTime(TimeOnly.Parse(end)), result.End);
        Assert.Equal(TimeSpan.FromMinutes(excludedMinutes), result.ExcludedBreaks);
        Assert.Equal(TimeSpan.FromHours(hours), result.End - result.Start - result.ExcludedBreaks);
    }

    [Theory]
    [InlineData(DayKind.Workday, 0)]
    [InlineData(DayKind.RestDay, 4)]
    public void Calculate_RejectsStartBeforeSevenThirty(DayKind kind, int hours)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            CountdownCalculator.Calculate(Date, new TimeOnly(7, 29, 59), kind, hours));
    }

    [Theory]
    [InlineData(DayKind.Workday, -1)]
    [InlineData(DayKind.Workday, 1)]
    [InlineData(DayKind.RestDay, -1)]
    [InlineData(DayKind.RestDay, 0)]
    [InlineData(DayKind.RestDay, 3)]
    [InlineData(DayKind.Unknown, 4)]
    [InlineData((DayKind)99, 4)]
    public void Calculate_RejectsInvalidKindOrOvertime(DayKind kind, int hours)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            CountdownCalculator.Calculate(Date, new TimeOnly(8, 0), kind, hours));
    }

    [Theory]
    [InlineData(DayKind.Workday)]
    [InlineData(DayKind.RestDay)]
    public void Calculate_RejectsHugeHoursWithoutIteratingOverThem(DayKind kind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CountdownCalculator.Calculate(Date, new TimeOnly(8, 0), kind, int.MaxValue));
    }

    [Fact]
    public void Calculate_AllowsLargeRepresentableHours()
    {
        var result = CountdownCalculator.Calculate(new DateOnly(1, 1, 1), new TimeOnly(8, 0), DayKind.RestDay, 22_500_000);

        Assert.Equal(new DateOnly(1, 1, 1).AddDays(1_000_000).ToDateTime(new TimeOnly(8, 0)), result.End);
        Assert.Equal(TimeSpan.FromHours(1_500_000), result.ExcludedBreaks);
    }

    [Fact]
    public void Calculate_RejectsOvertimeBeyondMaximumDate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CountdownCalculator.Calculate(DateOnly.MaxValue, new TimeOnly(20, 0), DayKind.RestDay, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CountdownCalculator.Calculate(DateOnly.MaxValue, new TimeOnly(8, 0), DayKind.Workday, 7));
    }

    [Fact]
    public void Calculate_AllowsMaximumDateWhenEndFits()
    {
        var result = CountdownCalculator.Calculate(DateOnly.MaxValue, new TimeOnly(19, 30), DayKind.RestDay, 4);

        Assert.Equal(DateOnly.MaxValue.ToDateTime(new TimeOnly(23, 30)), result.End);
    }

    [Fact]
    public void Calculate_PreservesSubsecondPrecision()
    {
        var start = new TimeOnly(12, 30).Add(TimeSpan.FromTicks(1));
        var result = CountdownCalculator.Calculate(Date, start, DayKind.RestDay, 4);

        Assert.Equal(Date.ToDateTime(new TimeOnly(17, 0)), result.End);
        Assert.Equal(TimeSpan.FromMinutes(30) - TimeSpan.FromTicks(1), result.ExcludedBreaks);
    }
}
