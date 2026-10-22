using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class WorkCountdownInputPolicyTests
{
    [Theory]
    [InlineData(9)]
    [InlineData(11)]
    public void NewDayAndManualRestMode_DoNotPrefillOvertime(int day)
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 10, day, 9, 0, 0), new MemoryStore(), false);
        Assert.True(string.IsNullOrWhiteSpace(vm.OvertimeText));
        vm.DayMode = CountdownDayMode.RestDay;
        Assert.True(string.IsNullOrWhiteSpace(vm.OvertimeText));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("  0  ")]
    public void Workday_BlankAndZeroStartWithoutOvertimeOrPendingChanges(string input)
    {
        var store = new MemoryStore();
        using var vm = new WorkCountdownViewModel(() => new(2026, 10, 9, 9, 0, 0), store, false)
        { OvertimeText = input };
        vm.StartCommand.Execute(null);
        Assert.NotNull(vm.Schedule);
        Assert.Empty(vm.ErrorMessage);
        Assert.Equal(0, store.State!.OvertimeHours);
        Assert.False(vm.HasOvertime);
        Assert.False(vm.HasPendingChanges);
        vm.OvertimeText = "";
        Assert.False(vm.HasPendingChanges);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("2")]
    public void RestDay_StillRequiresAtLeastFourHours(string input)
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 10, 11, 9, 0, 0), new MemoryStore(), false)
        { OvertimeText = input };
        vm.StartCommand.Execute(null);
        Assert.Null(vm.Schedule);
        Assert.Contains("至少 4 小时", vm.ErrorMessage);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("+2")]
    [InlineData("2.5")]
    [InlineData("abc")]
    public void InvalidOvertime_IsNotSilentlyTreatedAsZero(string input)
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 10, 9, 9, 0, 0), new MemoryStore(), false)
        { OvertimeText = input };
        vm.StartCommand.Execute(null);
        Assert.Null(vm.Schedule);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public void ClockInImport_AcceptsBlankWorkdayOvertime()
    {
        DateTime now = new(2026, 10, 9, 9, 0, 0);
        var store = new MemoryStore();
        using var vm = new WorkCountdownViewModel(() => now, store, false) { OvertimeText = "" };
        Assert.True(vm.ImportClockIn(now.AddMinutes(-15), () => true));
        Assert.Empty(vm.ErrorMessage);
        Assert.Equal(0, store.State!.OvertimeHours);
        Assert.Equal(new DateTime(2026, 10, 9, 17, 45, 0), vm.Schedule!.End);
    }

    [Fact]
    public void Finish_ClearsInputButPreservesAppliedPlanAndFrozenResult()
    {
        DateTime now = new(2026, 10, 9, 9, 0, 0);
        var store = new MemoryStore();
        using var vm = new WorkCountdownViewModel(() => now, store, false) { OvertimeText = "2" };
        vm.StartCommand.Execute(null);
        var schedule = vm.Schedule;
        vm.OvertimeText = "8";
        now = new(2026, 10, 9, 20, 10, 0);
        bool notified = false;
        vm.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(vm.OvertimeText);
        vm.FinishCommand.Execute(null);
        Assert.True(vm.IsFinished);
        Assert.True(notified);
        Assert.True(string.IsNullOrWhiteSpace(vm.OvertimeText));
        Assert.Same(schedule, vm.Schedule);
        Assert.Equal(2, store.State!.OvertimeHours);
        Assert.Equal(now, store.State.FinishedAt);
        Assert.Equal("00:10:00", vm.TimerText);
        Assert.Contains("2 小时", vm.OvertimeDurationText);
        Assert.False(vm.HasPendingChanges);
        vm.OvertimeText = "4";
        Assert.True(vm.HasPendingChanges);
    }

    [Fact]
    public void ReachingDeadline_StillKeepsOvertimeInputAndContinuesUnpaidTimer()
    {
        DateTime now = new(2026, 10, 9, 9, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false) { OvertimeText = "2" };
        vm.StartCommand.Execute(null);
        now = vm.Schedule!.End.AddMinutes(5);
        vm.Refresh();
        Assert.Equal("2", vm.OvertimeText);
        Assert.True(vm.IsOverdue);
        Assert.False(vm.IsFinished);
        Assert.Equal("00:05:00", vm.TimerText);
    }

    [Fact]
    public void RestoringFinishedTask_LeavesInputBlankAndRetainsOriginalOvertime()
    {
        var store = new MemoryStore { State = new(new(2026, 10, 9), "08:30", CountdownDayMode.Automatic, 2, new(2026, 10, 9, 20, 10, 0)) };
        using var vm = new WorkCountdownViewModel(() => new(2026, 10, 9, 21, 0, 0), store, false);
        Assert.True(vm.IsFinished);
        Assert.True(string.IsNullOrWhiteSpace(vm.OvertimeText));
        Assert.False(vm.HasPendingChanges);
        Assert.Equal(2, store.State!.OvertimeHours);
        Assert.Contains("2 小时", vm.OvertimeDurationText);
    }

    [Fact]
    public void NextDay_DoesNotCarryOverExpiredInputOrRewriteYesterdayPlan()
    {
        DateTime now = new(2026, 10, 9, 9, 0, 0);
        var store = new MemoryStore();
        using var vm = new WorkCountdownViewModel(() => now, store, false) { OvertimeText = "2" };
        vm.StartCommand.Execute(null);
        var schedule = vm.Schedule;
        now = new(2026, 10, 10, 8, 0, 0);
        vm.Refresh();
        Assert.True(string.IsNullOrWhiteSpace(vm.OvertimeText));
        Assert.Same(schedule, vm.Schedule);
        Assert.Equal(2, store.State!.OvertimeHours);
        Assert.False(vm.IsFinished);
        Assert.True(vm.IsOverdue);
        vm.StartCommand.Execute(null);
        Assert.Equal(new DateOnly(2026, 10, 10), store.State.WorkDate);
        Assert.Equal(0, store.State.OvertimeHours);
    }

    [Fact]
    public void ActiveOvernightPlan_IsNotClearedAtMidnight()
    {
        DateTime now = new(2026, 10, 9, 9, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false)
        { StartTimeText = "09:30", OvertimeText = "6" };
        vm.StartCommand.Execute(null);
        var schedule = vm.Schedule;
        now = new(2026, 10, 10, 0, 10, 0);
        vm.Refresh();
        Assert.Equal("6", vm.OvertimeText);
        Assert.Same(schedule, vm.Schedule);
        Assert.False(vm.IsOverdue);
        now = schedule!.End;
        vm.FinishCommand.Execute(null);
        Assert.True(string.IsNullOrWhiteSpace(vm.OvertimeText));
        Assert.True(vm.IsFinished);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void ReachingAnOvernightDeadline_DoesNotClearInputBeforeFinish(int overdueMinutes)
    {
        DateTime now = new(2026, 10, 9, 9, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false)
        { StartTimeText = "09:30", OvertimeText = "6" };
        vm.StartCommand.Execute(null);
        now = vm.Schedule!.End.AddMinutes(overdueMinutes);
        vm.Refresh();
        Assert.Equal("6", vm.OvertimeText);
        Assert.True(vm.IsOverdue);
        Assert.False(vm.IsFinished);
        Assert.Equal($"00:{overdueMinutes:00}:00", vm.TimerText);
    }

    [Fact]
    public void StartingANewDayWhileYesterdayPlanRuns_DoesNotInheritItsOvertime()
    {
        DateTime now = new(2026, 10, 9, 9, 0, 0);
        var store = new MemoryStore();
        using var vm = new WorkCountdownViewModel(() => now, store, false)
        { StartTimeText = "09:30", OvertimeText = "20" };
        vm.StartCommand.Execute(null);
        now = new(2026, 10, 10, 9, 0, 0);
        Assert.True(now < vm.Schedule!.End);
        vm.StartCommand.Execute(null);
        Assert.Empty(vm.ErrorMessage);
        Assert.Equal(new DateOnly(2026, 10, 10), store.State!.WorkDate);
        Assert.Equal(0, store.State.OvertimeHours);
    }

    [Fact]
    public void ImportingANewDayClockIn_DoesNotInheritYesterdayOvertime()
    {
        DateTime now = new(2026, 10, 9, 9, 0, 0);
        var store = new MemoryStore();
        using var vm = new WorkCountdownViewModel(() => now, store, false)
        { StartTimeText = "09:30", OvertimeText = "20" };
        vm.StartCommand.Execute(null);
        now = new(2026, 10, 10, 9, 0, 0);
        Assert.True(vm.ImportClockIn(now.AddMinutes(-15), () => true));
        Assert.Equal(new DateOnly(2026, 10, 10), store.State!.WorkDate);
        Assert.Equal(0, store.State.OvertimeHours);
    }

    [Fact]
    public void InputEditedToday_IsNotErasedByAnOldTaskRefresh()
    {
        DateTime now = new(2026, 10, 9, 9, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false) { OvertimeText = "2" };
        vm.StartCommand.Execute(null);
        now = new(2026, 10, 10, 8, 0, 0);
        vm.OvertimeText = "4";
        vm.Refresh();
        Assert.Equal("4", vm.OvertimeText);
    }

    [Fact]
    public void StartingNextDayWithoutATick_UsesZeroInsteadOfYesterdayOvertime()
    {
        DateTime now = new(2026, 10, 9, 9, 0, 0);
        var store = new MemoryStore();
        using var vm = new WorkCountdownViewModel(() => now, store, false) { OvertimeText = "2" };
        vm.StartCommand.Execute(null);
        now = new(2026, 10, 10, 9, 0, 0);
        vm.StartCommand.Execute(null);
        Assert.Equal(new DateOnly(2026, 10, 10), store.State!.WorkDate);
        Assert.Equal(0, store.State.OvertimeHours);
        Assert.True(string.IsNullOrWhiteSpace(vm.OvertimeText));
    }

    [Fact]
    public void ClockRollback_DoesNotEraseAFutureDatedUserInput()
    {
        DateTime now = new(2026, 10, 10, 9, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false) { OvertimeText = "4" };
        now = new(2026, 10, 9, 9, 0, 0);
        vm.Refresh();
        Assert.Equal("4", vm.OvertimeText);
    }

    private sealed class MemoryStore : ICountdownStateStore
    {
        public CountdownState? State;
        public CountdownState? Load() => State;
        public void Save(CountdownState state) => State = state;
        public void Clear() => State = null;
    }
}
