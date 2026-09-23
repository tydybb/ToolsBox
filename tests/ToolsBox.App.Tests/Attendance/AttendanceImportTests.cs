using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.Attendance;

public class AttendanceImportTests
{
    private static readonly DateTime Now = new(2026,9,22,9,0,0);
    private sealed class Memory : ICountdownStateStore
    {
        public CountdownState? State;
        public CountdownState? Load() => State;
        public void Save(CountdownState state) => State = state;
        public void Clear() => State = null;
    }
    [Fact]
    public void OvernightPendingTimeAndNewOvertimeEditsRequireConfirmation()
    {
        DateTime now=Now.AddDays(-1);
        using var vm=new WorkCountdownViewModel(()=>now,new Memory(),false);
        vm.StartTimeText="0800";vm.StartCommand.Execute(null);now=Now;vm.StartTimeText="08:15";
        bool asked=false;
        Assert.False(vm.ImportClockIn(now.AddMinutes(-10),()=>{asked=true;return false;}));Assert.True(asked);Assert.Equal("08:15",vm.StartTimeText);
        using var fresh=new WorkCountdownViewModel(()=>now,new Memory(),false);fresh.OvertimeText="2";asked=false;
        Assert.False(fresh.ImportClockIn(now.AddMinutes(-10),()=>{asked=true;return false;}));Assert.True(asked);
    }
    [Fact]
    public void ManualWorkdayOverrideStartsWorkdayNotDefaultRestDayOvertime()
    {
        DateTime now=new(2026,9,26,9,0,0);
        using var vm=new WorkCountdownViewModel(()=>now,new Memory(),false);
        Assert.Equal("4",vm.OvertimeText);
        Assert.True(vm.ImportClockIn(now.AddMinutes(-20),()=>true,CountdownDayMode.Workday));
        Assert.Equal("0",vm.OvertimeText);Assert.NotNull(vm.Schedule!.NormalEnd);
        Assert.Equal(new DateTime(2026,9,26,17,40,0),vm.Schedule.End);
    }
    [Fact]
    public void ImportsActualTimeAndStartsWithoutFakeManualTime()
    {
        using var vm = new WorkCountdownViewModel(() => Now, new Memory(), false);
        Assert.True(vm.ImportClockIn(Now.AddMinutes(-22), () => throw new Exception("No conflict")));
        Assert.Equal("08:38", vm.StartTimeText);
        Assert.Equal(new DateTime(2026,9,22,17,38,0), vm.Schedule!.End);
    }
    [Fact]
    public void ConflictDeclinePreservesPlanAndPendingInputs()
    {
        using var vm = new WorkCountdownViewModel(() => Now, new Memory(), false);
        vm.StartTimeText = "0800"; vm.StartCommand.Execute(null);
        vm.OvertimeText = "4";
        Assert.False(vm.ImportClockIn(Now.AddMinutes(-22), () => false));
        Assert.Equal(8, vm.Schedule!.Start.Hour);
        Assert.Equal("4", vm.OvertimeText);
        Assert.True(vm.ImportClockIn(Now.AddMinutes(-22), () => true));
        Assert.Equal("4", vm.OvertimeText);
        Assert.Equal(38, vm.Schedule!.Start.Minute);
    }
    [Fact]
    public void NewInputDuringConfirmationMustNotBeOverwritten()
    {
        using var vm = new WorkCountdownViewModel(() => Now, new Memory(), false);
        vm.StartTimeText="0800";vm.StartCommand.Execute(null);
        Assert.False(vm.ImportClockIn(Now.AddMinutes(-10),()=>{vm.StartTimeText="08:20";return true;}));
        Assert.Equal("08:20",vm.StartTimeText);
        Assert.Equal(8,vm.Schedule!.Start.Hour);Assert.Equal(0,vm.Schedule.Start.Minute);
    }
    [Fact]
    public void RejectsOldFutureOrBeforeMinimumAndDoesNotResumeFinished()
    {
        using var vm = new WorkCountdownViewModel(() => Now, new Memory(), false);
        Assert.False(vm.ImportClockIn(Now.AddDays(-1), () => true));
        Assert.False(vm.ImportClockIn(Now.AddMinutes(1), () => true));
        Assert.False(vm.ImportClockIn(Now.Date.AddHours(7), () => true));
        vm.StartTimeText="0800"; vm.StartCommand.Execute(null); vm.FinishCommand.Execute(null);
        Assert.False(vm.ImportClockIn(Now.AddMinutes(-10), () => true));
        Assert.True(vm.IsFinished);
    }
}
