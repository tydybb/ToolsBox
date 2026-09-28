using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class WorkCountdownFlavorTests
{
    [Fact]
    public void FlavorText_NearHoliday_TakesPriorityOverWeekdayCopy()
    {
        // 2026-09-18 是周五，但距中秋节 7 天，应显示节日倒计时而不是周五文案。
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 30, 0), new EmptyStore(), false);
        Assert.True(vm.HasFlavor);
        Assert.Contains("距离中秋节假期还有 7 天", vm.FlavorText);
        Assert.DoesNotContain("周五", vm.FlavorText);
    }

    [Fact]
    public void FlavorText_OnHolidayEve_UsesCountdownVoice()
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 24, 9, 30, 0), new EmptyStore(), false);
        Assert.Contains("明天开始中秋节假期", vm.FlavorText);
        Assert.Contains("节前最后一个工作日", vm.FlavorText);
    }

    [Fact]
    public void FlavorText_AfterFinish_SwitchesToOffWorkVoice()
    {
        DateTime now = new(2026, 9, 24, 9, 30, 0);
        using var vm = new WorkCountdownViewModel(() => now, new EmptyStore(), false);
        vm.StartTimeText = "0930";
        vm.StartCommand.Execute(null);
        Assert.Contains("明天开始中秋节假期", vm.FlavorText);
        now = new DateTime(2026, 9, 24, 18, 40, 0);
        vm.FinishCommand.Execute(null);

        Assert.True(vm.IsFinished);
        Assert.Equal("下班即放假，中秋节假期我来啦！", vm.FlavorText);
    }

    [Theory]
    [InlineData(2026, 11, 9, "周一")]
    [InlineData(2026, 11, 10, "周二")]
    [InlineData(2026, 11, 11, "周三")]
    [InlineData(2026, 11, 12, "周四")]
    [InlineData(2026, 11, 13, "周五")]
    [InlineData(2026, 11, 14, "周六")]
    [InlineData(2026, 11, 15, "周日")]
    public void FlavorText_OnOrdinaryWeek_ShowsWeekdayCopyForEveryDay(int year, int month, int day, string weekday)
    {
        using var vm = new WorkCountdownViewModel(() => new(year, month, day, 9, 30, 0), new EmptyStore(), false);
        Assert.True(vm.HasFlavor);
        Assert.Contains(weekday, vm.FlavorText);
    }

    [Fact]
    public void FlavorText_WithoutCalendarData_FallsBackToWeekdayCopy()
    {
        // 2027 年无节假日数据，但星期文案不依赖日历，仍应显示。
        using var vm = new WorkCountdownViewModel(() => new(2027, 9, 24, 9, 30, 0), new EmptyStore(), false);
        Assert.True(vm.HasFlavor);
        Assert.Contains("周五", vm.FlavorText);
        Assert.DoesNotContain("假期", vm.FlavorText);
    }

    [Fact]
    public void FlavorText_IsRaisedByDisplayRefresh()
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 9, 30, 0), new EmptyStore(), false);
        bool raised = false;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.FlavorText)) raised = true;
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
