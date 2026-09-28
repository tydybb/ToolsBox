using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.Core.Tests.WorkCountdown;

public sealed class OvertimeFunCopyTests
{
    [Theory]
    [InlineData(2026, 11, 9)]
    [InlineData(2026, 11, 14)]
    [InlineData(2027, 9, 24)]
    public void Countdown_AnyDate_ReturnsOvertimeVoice(int year, int month, int day)
    {
        string copy = OvertimeFunCopy.Countdown(new DateOnly(year, month, day));
        Assert.False(string.IsNullOrWhiteSpace(copy));
        Assert.Contains("班", copy);
    }

    [Fact]
    public void Countdown_RotatesAcrossWeeks_ButStaysStableWithinADay()
    {
        var day1 = new DateOnly(2026, 11, 13);
        var day2 = day1.AddDays(7);
        Assert.Equal(OvertimeFunCopy.Countdown(day1), OvertimeFunCopy.Countdown(day1.AddDays(0)));
        Assert.NotEqual(OvertimeFunCopy.Countdown(day1), OvertimeFunCopy.Countdown(day2));
    }

    [Fact]
    public void Countdown_DoesNotUseWeekdayLazeVoice()
    {
        // 加班语气不能混入躺平系文案，否则与同屏加班提示仍显打架。
        var date = new DateOnly(2026, 11, 14);
        string copy = OvertimeFunCopy.Countdown(date);
        Assert.DoesNotContain("自然醒", copy);
        Assert.DoesNotContain("赖床", copy);
        Assert.DoesNotContain("收工", copy);
        Assert.DoesNotContain("下班", copy);
    }
}
