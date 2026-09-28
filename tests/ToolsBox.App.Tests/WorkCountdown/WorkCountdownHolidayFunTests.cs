using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class WorkCountdownHolidayFunTests
{
    [Fact]
    public void CountdownText_WithinSevenDaysOfHoliday_CountsDownToIt()
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 30, 0), new EmptyStore(), false);
        Assert.True(vm.HasHolidayFun);
        Assert.Contains("距离中秋节假期还有 7 天", vm.HolidayFunText);
    }

    [Fact]
    public void CountdownText_OnHolidayEve_UsesCountdownVoice()
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 24, 9, 30, 0), new EmptyStore(), false);
        Assert.Contains("明天开始中秋节假期", vm.HolidayFunText);
        Assert.Contains("节前最后一个工作日", vm.HolidayFunText);
    }

    [Fact]
    public void HolidayFunText_AfterFinish_SwitchesToOffWorkVoice()
    {
        DateTime now = new(2026, 9, 24, 9, 30, 0);
        using var vm = new WorkCountdownViewModel(() => now, new EmptyStore(), false);
        vm.StartTimeText = "0930";
        vm.StartCommand.Execute(null);
        Assert.Contains("明天开始中秋节假期", vm.HolidayFunText);
        now = new DateTime(2026, 9, 24, 18, 40, 0);
        vm.FinishCommand.Execute(null);

        Assert.True(vm.IsFinished);
        Assert.Equal("下班即放假，中秋节假期我来啦！", vm.HolidayFunText);
    }

    [Fact]
    public void HolidayFunText_OnOrdinaryWorkday_StaysEmpty()
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 11, 15, 9, 30, 0), new EmptyStore(), false);
        Assert.Equal("", vm.HolidayFunText);
        Assert.False(vm.HasHolidayFun);
    }

    [Fact]
    public void HolidayFunText_WithoutCalendarData_DegradesToEmpty()
    {
        using var vm = new WorkCountdownViewModel(() => new(2027, 9, 24, 9, 30, 0), new EmptyStore(), false);
        Assert.Equal("", vm.HolidayFunText);
        Assert.False(vm.HasHolidayFun);
    }

    [Fact]
    public void HolidayFunText_IsRaisedByDisplayRefresh()
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 30, 0), new EmptyStore(), false);
        bool raised = false;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.HolidayFunText)) raised = true;
        };
        vm.StartTimeText = "09:30";
        Assert.True(raised);
    }

    private sealed class EmptyStore : ICountdownStateStore
    {
        public CountdownState? Load() => null;
        public void Save(CountdownState state) { }
        public void Clear() { }
    }
}
