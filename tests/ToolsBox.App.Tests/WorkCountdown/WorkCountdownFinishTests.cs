using System.Reflection;
using ToolsBox.App.WorkCountdown;
using ToolsBox.App.Infrastructure;
using System.Windows.Threading;
using System.IO;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class WorkCountdownFinishTests
{
    [Theory]
    [InlineData("09:30", 18, 0, 0, true, "00:30:00")]
    [InlineData("09:30", 18, 30, 0, false, "00:00:00")]
    [InlineData("09:30", 18, 31, 0, false, "00:01:00")]
    [InlineData("09:45", 18, 30, 0, false, "00:00:00")]
    [InlineData("09:30", 18, 30, 2, false, "02:30:00")]
    [InlineData("09:30", 21, 15, 2, false, "00:15:00")]
    public void Finish_UsesNormalEndForAttendanceAndFinalEndForFrozenTimer(string start, int hour, int minute, int overtime, bool early, string timer)
    {
        var now = new DateTime(2026, 9, 18, hour, minute, 0);
        using var vm = Started(() => now, new Store(), start, overtime);
        Assert.Equal(start == "09:45", vm.Schedule!.IsLate);
        vm.FinishCommand.Execute(null);
        Assert.True(vm.IsFinished);
        Assert.Equal(now, vm.FinishedAt);
        Assert.Equal(early, vm.IsEarlyDeparture);
        Assert.Equal(overtime > 0 && hour < 21, vm.HasIncompleteOvertime);
        Assert.Equal(timer, vm.TimerText);
        Assert.Contains("计时已停止", vm.FinishedTimerLabel);
        Assert.Contains(now.ToString("yyyy-MM-dd HH:mm:ss"), vm.FinishDetails);
        if (!early) Assert.Equal("已下班，抓紧回家吧！", vm.FinishMessage);
        now = now.AddDays(1);
        vm.Refresh();
        Assert.Equal(timer, vm.TimerText);
        Assert.False(vm.IsOverdue);
    }

    [Fact]
    public void Finish_UsesFreshClockAndAppliedPlanAndFiresOnceWithoutDeadlineReminder()
    {
        var now = new DateTime(2026, 9, 18, 10, 0, 0);
        using var vm = Started(() => now, new Store());
        int finished = 0, reached = 0;
        vm.WorkFinished += (_, _) => finished++;
        vm.OffWorkReached += (_, _) => reached++;
        vm.StartTimeText = "07:30";
        vm.OvertimeText = "4";
        vm.DayMode = CountdownDayMode.RestDay;
        now = new(2026, 9, 18, 18, 31, 0);
        vm.FinishCommand.Execute(null);
        vm.FinishCommand.Execute(null);
        Assert.Equal(now, vm.FinishedAt);
        Assert.Equal(1, finished);
        Assert.Equal(0, reached);
        Assert.False(vm.HasIncompleteOvertime);
        Assert.Equal("00:01:00", vm.TimerText);
    }

    [Fact]
    public void RestDayFinish_IsNotEarlyButReportsIncompleteOvertime()
    {
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 19, 10, 0, 0), new Store(), false);
        vm.DayMode = CountdownDayMode.RestDay;
        vm.StartTimeText = "09:30";
        vm.OvertimeText = "4";
        vm.StartCommand.Execute(null);
        vm.FinishCommand.Execute(null);
        Assert.False(vm.IsEarlyDeparture);
        Assert.True(vm.HasIncompleteOvertime);
        Assert.Contains("计划加班未完成", vm.FinishDetails);
    }

    [Fact]
    public void EarlyDeparture_RoundsPartialMinuteUp()
    {
        using var vm = Started(() => new(2026, 9, 18, 18, 29, 59), new Store());
        vm.FinishCommand.Execute(null);
        Assert.Equal("你早退了，距离正常下班还差 1 分钟。", vm.FinishMessage);
    }

    [Fact]
    public void FinishCommand_RejectsIdleFutureStartClockRollbackAndDisposed()
    {
        var now = new DateTime(2026, 9, 18, 8, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new Store(), false);
        Assert.False(vm.FinishCommand.CanExecute(null));
        vm.FinishCommand.Execute(null);
        vm.StartTimeText = "09:30";
        vm.DayMode = CountdownDayMode.Workday;
        vm.OvertimeText = "0";
        vm.StartCommand.Execute(null);
        Assert.False(vm.FinishCommand.CanExecute(null));
        vm.FinishCommand.Execute(null);
        Assert.False(vm.IsFinished);
        now = now.AddHours(2);
        int changed = 0;
        vm.FinishCommand.CanExecuteChanged += (_, _) => changed++;
        vm.Refresh();
        Assert.True(vm.FinishCommand.CanExecute(null));
        Assert.True(changed > 0);
        now = now.AddHours(-2);
        vm.FinishCommand.Execute(null);
        Assert.False(vm.IsFinished);
        now = now.AddHours(2);
        vm.Dispose();
        Assert.False(vm.FinishCommand.CanExecute(null));
        vm.FinishCommand.Execute(null);
        Assert.False(vm.IsFinished);
    }

    [Fact]
    public void Finish_SaveFailureStillStopsAndNotifies()
    {
        var store = new Store();
        using var vm = Started(() => new(2026, 9, 18, 18, 0, 0), store);
        store.FailSave = true;
        int events = 0;
        vm.WorkFinished += (_, _) => events++;
        vm.FinishCommand.Execute(null);
        Assert.True(vm.IsFinished);
        Assert.Equal(1, events);
        Assert.Contains("无法保存", vm.ErrorMessage);
        Assert.Contains("重启", vm.ErrorMessage);
    }

    [Fact]
    public void FinishedRestore_FreezesAndInvalidRestartPreservesCompletionThenValidRestartAndResetClearIt()
    {
        var now = new DateTime(2026, 9, 18, 19, 0, 0);
        var store = new Store { State = new(new(2026, 9, 18), "09:30", CountdownDayMode.Workday, 2, new(2026, 9, 18, 18, 30, 0)) };
        using var vm = new WorkCountdownViewModel(() => now, store, false);
        int events = 0;
        vm.OffWorkReached += (_, _) => events++;
        vm.WorkFinished += (_, _) => events++;
        Assert.Equal("02:30:00", vm.TimerText);
        vm.Refresh();
        Assert.Equal(0, events);
        vm.StartTimeText = "invalid";
        vm.StartCommand.Execute(null);
        Assert.True(vm.IsFinished);
        Assert.Equal("02:30:00", vm.TimerText);
        vm.StartTimeText = "09:30";
        vm.StartCommand.Execute(null);
        Assert.False(vm.IsFinished);
        Assert.Null(store.State!.FinishedAt);
        vm.FinishCommand.Execute(null);
        vm.ResetCommand.Execute(null);
        Assert.False(vm.IsFinished);
        Assert.Null(store.State);
        Assert.Equal("--:--:--", vm.TimerText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restore_DropsOldDateAndWarnsForFinishBeforeStart(bool old)
    {
        var store = new Store { State = new(new(2026, 9, old ? 17 : 18), "09:30", CountdownDayMode.Workday, 0, new(2026, 9, 18, 9, 0, 0)) };
        using var vm = new WorkCountdownViewModel(() => new(2026, 9, 18, 10, 0, 0), store, false);
        Assert.Null(vm.Schedule);
        Assert.False(vm.IsFinished);
        Assert.Equal(!old, vm.ErrorMessage.Contains("恢复"));
    }

    [Fact]
    public Task DispatcherTimer_StopsOnFinishAndRestoreRestartsOnStartAndReset() => WpfTestThread.RunAsync(() =>
    {
        var store = new Store();
        using var vm = Started(() => new(2026, 9, 18, 18, 0, 0), store, timer: true);
        var timer = (DispatcherTimer)typeof(WorkCountdownViewModel).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        Assert.True(timer.IsEnabled);
        vm.FinishCommand.Execute(null);
        Assert.False(timer.IsEnabled);
        using var restored = new WorkCountdownViewModel(() => new(2026, 9, 18, 20, 0, 0), store);
        var restoredTimer = (DispatcherTimer)typeof(WorkCountdownViewModel).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(restored)!;
        Assert.False(restoredTimer.IsEnabled);
        vm.StartTimeText = "bad";
        vm.StartCommand.Execute(null);
        Assert.False(timer.IsEnabled);
        vm.StartTimeText = "09:30";
        vm.StartCommand.Execute(null);
        Assert.True(timer.IsEnabled);
        vm.FinishCommand.Execute(null);
        vm.ResetCommand.Execute(null);
        Assert.True(timer.IsEnabled);
        vm.Dispose();
        Assert.False(timer.IsEnabled);
        return Task.CompletedTask;
    });

    private static WorkCountdownViewModel Started(Func<DateTime> now, Store store, string start = "09:30", int overtime = 0, bool timer = false)
    {
        var vm = new WorkCountdownViewModel(now, store, timer) { StartTimeText = start, DayMode = CountdownDayMode.Workday, OvertimeText = overtime.ToString() };
        vm.StartCommand.Execute(null);
        return vm;
    }

    [Fact]
    public void Finish_FreezesTimeAndReportsEarlyDeparture()
    {
        var now = new DateTime(2026, 9, 18, 18, 0, 0);
        using var vm = new WorkCountdownViewModel(() => now, new Store(), false);
        vm.StartTimeText = "09:30";
        vm.DayMode = CountdownDayMode.Workday;
        vm.OvertimeText = "0";
        vm.StartCommand.Execute(null);
        var command = typeof(WorkCountdownViewModel).GetProperty("FinishCommand")?.GetValue(vm) as RelayCommand;
        Assert.NotNull(command);
        command.Execute(null);
        Assert.Contains("你早退了", vm.StatusText);
        Assert.Equal("00:30:00", vm.TimerText);
        now = now.AddHours(4);
        vm.Refresh();
        Assert.Equal("00:30:00", vm.TimerText);
        Assert.False(vm.IsOverdue);
    }

    private sealed class Store : ICountdownStateStore
    {
        public CountdownState? State;
        public bool FailSave;
        public CountdownState? Load() => State;
        public void Save(CountdownState state) { if (FailSave) throw new IOException("save failed"); State = state; }
        public void Clear() => State = null;
    }
}
