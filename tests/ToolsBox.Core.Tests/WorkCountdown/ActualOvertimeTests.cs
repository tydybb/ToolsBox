using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.Core.Tests.WorkCountdown;

public sealed class ActualOvertimeTests
{
    [Theory]
    [InlineData("2026-09-18T17:00:00", "2026-09-18T16:30:00", 0)]
    [InlineData("2026-09-18T17:00:00", "2026-09-18T17:00:00", 0)]
    [InlineData("2026-09-18T17:00:00", "2026-09-18T18:15:00", 75)]
    [InlineData("2026-09-18T17:00:00", "2026-09-18T19:00:00", 120)]
    [InlineData("2026-09-18T17:00:00", "2026-09-18T19:15:00", 120)]
    [InlineData("2026-09-18T17:00:00", "2026-09-18T19:30:00", 120)]
    [InlineData("2026-09-18T17:00:00", "2026-09-18T20:00:00", 150)]
    [InlineData("2026-09-19T08:00:00", "2026-09-19T12:00:00", 240)]
    [InlineData("2026-09-19T08:00:00", "2026-09-19T12:30:00", 240)]
    [InlineData("2026-09-19T08:00:00", "2026-09-19T13:00:00", 240)]
    [InlineData("2026-09-19T08:00:00", "2026-09-19T20:00:00", 630)]
    [InlineData("2026-09-19T12:15:00", "2026-09-19T13:15:00", 15)]
    [InlineData("2026-09-19T19:15:00", "2026-09-19T19:45:00", 15)]
    [InlineData("2026-09-30T18:30:00", "2026-10-01T14:00:00", 1080)]
    [InlineData("2026-09-30T20:00:00", "2026-10-02T20:00:00", 2700)]
    public void CalculatesActualEffectiveTimeIncludingUnplannedAndCrossMonthWork(
        string start, string finished, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), CountdownCalculator.CalculateActualOvertime(
            DateTime.Parse(start), DateTime.Parse(finished)));
    }

    [Fact]
    public void PreservesSingleTickPrecisionAndSupportsEntireDateRange()
    {
        var start = new DateTime(2026, 9, 19, 13, 0, 0);
        Assert.Equal(TimeSpan.FromTicks(1), CountdownCalculator.CalculateActualOvertime(start, start.AddTicks(1)));
        var expected = TimeSpan.FromTicks(DateTime.MaxValue.Ticks - 3_652_059L * TimeSpan.TicksPerMinute * 90);
        Assert.Equal(expected, CountdownCalculator.CalculateActualOvertime(DateTime.MinValue, DateTime.MaxValue));
    }
}
