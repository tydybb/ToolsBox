using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class WorkCountdownCalendarPolicyTests
{
    [Fact]
    public void FridayBeforeMakeup_MentionsTomorrowBeforeAndAfterFinish()
    {
        DateTime now = new(2026, 10, 9, 9, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false);
        vm.StartCommand.Execute(null);
        Assert.Contains("明天调休补班", vm.FlavorText);
        Assert.Contains("明天调休补班", vm.Reaction.Caption);
        now = new(2026, 10, 9, 18, 0, 0);
        vm.FinishCommand.Execute(null);
        Assert.Contains("明天调休补班", vm.FlavorText);
        Assert.Contains("明天调休补班", vm.Reaction.Caption);
        Assert.DoesNotContain("48", vm.FlavorText);
    }

    [Fact]
    public void CompletedManualRestTask_UsesAppliedKindNotTheWeekdayDefault()
    {
        DateTime now = new(2026, 11, 9, 9, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false)
        { DayMode = CountdownDayMode.RestDay, OvertimeText = "4" };
        vm.StartCommand.Execute(null);
        now = new(2026, 11, 9, 15, 0, 0);
        vm.FinishCommand.Execute(null);
        Assert.Contains("休息", vm.FlavorText);
        Assert.DoesNotContain("周一首战", vm.FlavorText);
    }

    [Theory]
    [InlineData(10, 10, "调休补班收工")]
    [InlineData(9, 24, "下班即放假")]
    [InlineData(10, 8, "打工人回归")]
    public void ManualRestOverride_DoesNotReturnToWorkerOnlyHolidayCopy(int month, int day, string workerCopy)
    {
        DateTime now = new(2026, month, day, 9, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false)
        { DayMode = CountdownDayMode.RestDay, OvertimeText = "4" };
        vm.StartCommand.Execute(null);
        now = vm.Schedule!.End;
        vm.FinishCommand.Execute(null);
        Assert.Contains("休息日加班", vm.FlavorText);
        Assert.DoesNotContain(workerCopy, vm.FlavorText);
    }

    [Fact]
    public void ManualSaturdayWorkTask_DoesNotSuggestSleepingInToday()
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 11, 14, 9, 0, 0), new MemoryStore(), false)
        { DayMode = CountdownDayMode.Workday, OvertimeText = "0" };
        vm.StartCommand.Execute(null);
        Assert.Contains("上班", vm.FlavorText);
        Assert.DoesNotContain("赖床", vm.FlavorText);
        Assert.DoesNotContain("自然醒", vm.FlavorText);
    }

    [Fact]
    public void EditingDayModeDraft_DoesNotReplaceRunningTaskCopy()
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 10, 9, 9, 0, 0), new MemoryStore(), false);
        vm.StartCommand.Execute(null);
        string flavor = vm.FlavorText, caption = vm.Reaction.Caption;
        vm.DayMode = CountdownDayMode.RestDay;
        Assert.Equal(flavor, vm.FlavorText);
        Assert.Equal(caption, vm.Reaction.Caption);
        Assert.Contains("明天调休补班", vm.FlavorText);
    }

    private sealed class MemoryStore : ICountdownStateStore
    {
        public CountdownState? State;
        public CountdownState? Load() => State;
        public void Save(CountdownState state) => State = state;
        public void Clear() => State = null;
    }
}
