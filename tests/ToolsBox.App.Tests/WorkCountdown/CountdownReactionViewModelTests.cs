using ToolsBox.App.WorkCountdown;
using ToolsBox.Core.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class CountdownReactionViewModelTests
{
    [Fact]
    public Task Constructor_InitializesReactionFromTheRestoredAppliedTask() => WpfTestThread.RunAsync(() =>
    {
        DateTime now = new(2026, 11, 9, 14, 0, 0);
        var store = new Store { State = new(new(2026, 11, 9), "08:30", CountdownDayMode.Workday, 0) };
        using var vm = new WorkCountdownViewModel(() => now, store, false);

        Assert.Equal("xiaohe/tired", vm.Reaction.AnimationKey);
        Assert.Equal("小河", vm.Reaction.CharacterName);
        Assert.True(vm.AnimationEnabled);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PendingInputs_DoNotChangeTheReactionUntilApplied() => WpfTestThread.RunAsync(() =>
    {
        DateTime now = new(2026, 11, 9, 9, 0, 0);
        using var vm = Started(() => now);
        CountdownReaction original = vm.Reaction;
        int changes = 0;
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.Reaction)) changes++; };

        vm.StartTimeText = "07:30";
        vm.OvertimeText = "22";
        vm.DayMode = CountdownDayMode.RestDay;
        vm.Refresh();

        Assert.Same(original, vm.Reaction);
        Assert.Equal(0, changes);
        Assert.True(vm.HasPendingChanges);
        Assert.Equal("输入已变更；仍显示上次任务，请点击“开始 / 重新计算”应用。", vm.PendingText);

        vm.StartCommand.Execute(null);

        Assert.Equal("tianzhong/stressed", vm.Reaction.AnimationKey);
        Assert.Contains("休息日加班", vm.Reaction.Caption);
        Assert.Equal(1, changes);
        return Task.CompletedTask;
    });

    [Fact]
    public Task IdleInputs_DoNotInventAnAppliedTask() => WpfTestThread.RunAsync(() =>
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 10, 3, 9, 0, 0), new Store(), false);
        CountdownReaction original = vm.Reaction;

        vm.DayMode = CountdownDayMode.Workday;
        vm.StartTimeText = "07:30";
        vm.OvertimeText = "15";

        Assert.Same(original, vm.Reaction);
        Assert.Equal("xiuzhen/happy", vm.Reaction.AnimationKey);
        Assert.Null(vm.Schedule);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Refresh_RetainsTheReactionObjectWithoutAStageChange() => WpfTestThread.RunAsync(() =>
    {
        DateTime now = new(2026, 11, 9, 9, 0, 0);
        using var vm = Started(() => now);
        CountdownReaction original = vm.Reaction;
        int changes = 0;
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.Reaction)) changes++; };

        for (int second = 0; second < 3; second++)
        {
            now = now.AddSeconds(1);
            vm.Refresh();
            Assert.Same(original, vm.Reaction);
        }

        Assert.Equal(0, changes);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Refresh_ChangesTheReactionOnceWhenTheStageBoundaryIsCrossed() => WpfTestThread.RunAsync(() =>
    {
        DateTime now = new(2026, 11, 9, 10, 29, 59);
        using var vm = Started(() => now);
        Assert.Equal("lipu/calm", vm.Reaction.AnimationKey);
        int changes = 0;
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.Reaction)) changes++; };

        now = now.AddSeconds(1);
        vm.Refresh();
        CountdownReaction changed = vm.Reaction;
        now = now.AddSeconds(1);
        vm.Refresh();

        Assert.Equal("lipu/tired", changed.AnimationKey);
        Assert.Same(changed, vm.Reaction);
        Assert.Equal(1, changes);
        return Task.CompletedTask;
    });

    [Fact]
    public Task CrossMidnightReaction_UsesTheAppliedTaskDateAndKind() => WpfTestThread.RunAsync(() =>
    {
        DateTime now = new(2026, 9, 24, 22, 0, 0);
        using var vm = Started(() => now, overtime: 8);

        now = new(2026, 9, 25, 0, 30, 0);
        vm.Refresh();

        Assert.Equal("tianzhong/stressed", vm.Reaction.AnimationKey);
        Assert.Contains("周四", vm.Reaction.Caption);
        Assert.DoesNotContain("假期加班", vm.Reaction.Caption);
        Assert.Contains("明天休息", vm.Reaction.Caption);
        Assert.True(vm.HasPendingChanges);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(16, "提前收工")]
    [InlineData(18, "下班")]
    public Task Finish_FreezesTheReactionAcrossClockAndInputChanges(int finishedHour, string caption) => WpfTestThread.RunAsync(() =>
    {
        DateTime now = new(2026, 11, 9, 9, 0, 0);
        using var vm = Started(() => now);
        now = new(2026, 11, 9, finishedHour, 0, 0);
        vm.FinishCommand.Execute(null);
        CountdownReaction finished = vm.Reaction;
        int changes = 0;
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.Reaction)) changes++; };

        now = now.AddDays(7);
        vm.StartTimeText = "09:30";
        vm.DayMode = CountdownDayMode.RestDay;
        vm.OvertimeText = "22";
        vm.Refresh();

        Assert.EndsWith("/happy", finished.AnimationKey);
        Assert.Contains(caption, finished.Caption);
        Assert.Same(finished, vm.Reaction);
        Assert.Equal(0, changes);
        return Task.CompletedTask;
    });

    [Fact]
    public Task FinishedRestore_InitializesAFrozenHappyReaction() => WpfTestThread.RunAsync(() =>
    {
        DateOnly date = new(2026, 11, 13);
        DateTime finished = date.ToDateTime(new(17, 30));
        var store = new Store { State = new(date, "08:30", CountdownDayMode.Workday, 15, finished) };
        DateTime now = date.ToDateTime(new(23, 0));
        using var vm = new WorkCountdownViewModel(() => now, store, false);
        CountdownReaction reaction = vm.Reaction;

        now = now.AddDays(1);
        vm.Refresh();

        Assert.Equal("xiaohe/happy", reaction.AnimationKey);
        Assert.Same(reaction, vm.Reaction);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ManualWorkdayOnHoliday_AppliesWorkingReaction() => WpfTestThread.RunAsync(() =>
    {
        using var vm = Started(() => new(2026, 10, 3, 9, 0, 0));

        Assert.Equal("tianzhong/calm", vm.Reaction.AnimationKey);
        Assert.Contains("假期上班", vm.Reaction.Caption);
        return Task.CompletedTask;
    });

    [Fact]
    public Task AnimationEnabled_OnlyNotifiesOnChangeAndDoesNotChangeTheReaction() => WpfTestThread.RunAsync(() =>
    {
        using var vm = Started(() => new(2026, 11, 9, 9, 0, 0));
        CountdownReaction reaction = vm.Reaction;
        int enabledChanges = 0, reactionChanges = 0;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.AnimationEnabled)) enabledChanges++;
            if (args.PropertyName == nameof(vm.Reaction)) reactionChanges++;
        };

        vm.AnimationEnabled = false;
        vm.AnimationEnabled = false;
        vm.AnimationEnabled = true;

        Assert.True(vm.AnimationEnabled);
        Assert.Equal(2, enabledChanges);
        Assert.Equal(0, reactionChanges);
        Assert.Same(reaction, vm.Reaction);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DisposedModel_RefreshDoesNotChangeTheReaction() => WpfTestThread.RunAsync(() =>
    {
        DateTime now = new(2026, 11, 9, 9, 0, 0);
        using var vm = Started(() => now);
        CountdownReaction reaction = vm.Reaction;
        vm.Dispose();

        now = now.AddHours(10);
        vm.Refresh();

        Assert.Same(reaction, vm.Reaction);
        return Task.CompletedTask;
    });

    private static WorkCountdownViewModel Started(Func<DateTime> now, int overtime = 0)
    {
        var vm = new WorkCountdownViewModel(now, new Store(), false)
        {
            StartTimeText = "08:30",
            DayMode = CountdownDayMode.Workday,
            OvertimeText = overtime.ToString()
        };
        vm.StartCommand.Execute(null);
        return vm;
    }

    private sealed class Store : ICountdownStateStore
    {
        public CountdownState? State;
        public CountdownState? Load() => State;
        public void Save(CountdownState state) => State = state;
        public void Clear() => State = null;
    }
}
