using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class WorkCountdownReminderTests
{
    [Fact]
    public void FinalEnd_RemindsOnce_DespiteReentrantRefreshClockRollbackAndSameTaskRecalculation()
    {
        DateTime now = new(2026, 9, 18, 9, 30, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false);
        int reminders = 0;
        Listen(vm, (_, _) => { reminders++; vm.Refresh(); });
        vm.Refresh();
        Assert.Equal(0, reminders);
        vm.StartTimeText = "09:30";
        vm.OvertimeText = "2";
        vm.StartCommand.Execute(null);
        now = new(2026, 9, 18, 19, 15, 0);
        vm.Refresh();
        Assert.Equal(0, reminders);
        now = vm.Schedule!.End;
        vm.Refresh();
        Assert.Equal(1, reminders);
        vm.Refresh();
        vm.StartTimeText = "0930";
        vm.StartCommand.Execute(null);
        Assert.Equal(1, reminders);
        now = now.AddHours(-1);
        vm.Refresh();
        now = now.AddHours(3);
        vm.Refresh();
        vm.OvertimeText = "1";
        vm.StartCommand.Execute(null);
        Assert.NotEmpty(vm.ErrorMessage);
        vm.Refresh();
        Assert.Equal(1, reminders);
    }

    [Fact]
    public void SuccessfulNewTask_AndResetAllowAnotherReminder()
    {
        DateTime now = new(2026, 9, 18, 18, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false);
        int reminders = 0;
        Listen(vm, (_, _) => reminders++);
        vm.StartCommand.Execute(null);
        Assert.Equal(1, reminders);

        vm.OvertimeText = "2";
        vm.StartCommand.Execute(null);
        Assert.Equal(1, reminders);
        now = vm.Schedule!.End;
        vm.Refresh();
        Assert.Equal(2, reminders);
        vm.ResetCommand.Execute(null);
        vm.Refresh();
        Assert.Equal(2, reminders);
        vm.StartCommand.Execute(null);
        Assert.Equal(3, reminders);
    }

    [Fact]
    public void RestoreExpiredTask_RemindsOnFirstRefreshOnly()
    {
        var store = new MemoryStore { State = new(new(2026, 9, 18), "09:30", CountdownDayMode.Workday, 2) };
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 22, 0, 0), store, false);
        int reminders = 0;
        Listen(vm, (_, _) => reminders++);
        Assert.Equal(0, reminders);
        vm.Refresh();
        vm.Refresh();
        Assert.Equal(1, reminders);
        vm.Dispose();
        vm.Refresh();
        Assert.Equal(1, reminders);
    }

    [Fact]
    public void ReplacingTaskBeforeNextTick_DoesNotRemindForSupersededEnd()
    {
        DateTime now = new(2026, 9, 18, 9, 30, 0);
        using var vm = new WorkCountdownViewModel(() => now, new MemoryStore(), false);
        int reminders = 0;
        Listen(vm, (_, _) => reminders++);
        vm.StartCommand.Execute(null);
        now = new(2026, 9, 18, 18, 0, 0);
        vm.OvertimeText = "2";
        vm.StartCommand.Execute(null);
        Assert.Equal(0, reminders);
        now = vm.Schedule!.End;
        vm.Refresh();
        Assert.Equal(1, reminders);
    }

    private static void Listen(WorkCountdownViewModel vm, EventHandler handler)
    {
        vm.OffWorkReached += handler;
    }

    private sealed class MemoryStore : ICountdownStateStore
    {
        public CountdownState? State { get; set; }
        public CountdownState? Load() => State;
        public void Save(CountdownState state) => State = state;
        public void Clear() => State = null;
    }
}
