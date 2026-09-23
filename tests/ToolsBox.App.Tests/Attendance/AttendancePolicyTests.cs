using ToolsBox.App.Attendance;

namespace ToolsBox.App.Tests.Attendance;

public class AttendancePolicyTests
{
    private static DateTime At(int h, int m, int s = 0) => new(2026, 9, 22, h, m, s);

    [Fact]
    public void PollingAndFinalDeadlineAreIndependent()
    {
        var p = new AttendancePolicy();
        Assert.False(p.TryBeginCheck(At(7,29), true, true, true, false));
        Assert.True(p.TryBeginCheck(At(7,30), true, true, true, false));
        Assert.False(p.TryBeginCheck(At(7,31), true, true, true, false));
        Assert.True(p.TryBeginCheck(At(7,35), true, true, true, false));
        Assert.True(p.TryBeginCheck(At(9,24), true, true, true, true));
        Assert.True(p.TryBeginCheck(At(9,25), true, true, true, false));
        Assert.False(p.TryBeginCheck(At(9,31), true, true, true, true));
    }

    [Fact]
    public void ReminderNeverRepeatsFinalOrLeaksPastDeadline()
    {
        var p = new AttendancePolicy();
        Assert.True(p.TryRemind(At(9,24)));
        Assert.True(p.TryRemind(At(9,25)));
        Assert.False(p.TryRemind(At(9,25,20)));
        Assert.False(p.TryRemind(At(9,26)));
    }

    [Fact]
    public void NoReadsOnDisabledRestOrLockedAndConfirmationResetsTomorrow()
    {
        var p = new AttendancePolicy();
        Assert.False(p.TryBeginCheck(At(8,0), false, true, true, true));
        Assert.False(p.TryBeginCheck(At(8,0), true, false, true, true));
        Assert.False(p.TryBeginCheck(At(8,0), true, true, false, true));
        Assert.False(p.TryRemind(At(8,0), DateOnly.FromDateTime(At(8,0))));
        Assert.True(p.TryRemind(At(8,0).AddDays(1), DateOnly.FromDateTime(At(8,0))));
    }

    [Fact]
    public void WorkdaySelectionHonorsHolidayMakeupAndTodayOnlyOverride()
    {
        var options = new AttendanceOptions();
        Assert.True(options.IsWorkday(new DateOnly(2026,9,20)));
        Assert.False(options.IsWorkday(new DateOnly(2026,9,25)));
        options = options with { OverrideDate = new(2026,9,25), OverrideWorkday = true };
        Assert.True(options.IsWorkday(new DateOnly(2026,9,25)));
        Assert.False(options.IsWorkday(new DateOnly(2026,9,26)));
        Assert.False(options.IsWorkday(new DateOnly(2027,1,4)));
    }
}
