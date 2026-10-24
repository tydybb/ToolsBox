using System.IO;
using ToolsBox.App.Attendance;
using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.Attendance;

public sealed class AttendanceBridgeTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("4", true)]
    public void WeekendManualResultFillsActualTimeWithoutInventingOvertime(string overtime, bool starts)
    {
        string root = Path.Combine(Path.GetTempPath(), "ToolsBox-weekend-import-" + Guid.NewGuid().ToString("N"));
        DateTime now = new(2026, 10, 24, 9, 0, 0), at = now.AddMinutes(-18);
        var store = new AttendanceStore(root);
        try
        {
            store.Update(o => o with { Enabled = true });
            store.SaveSnapshot(new(now, at, "", "synthetic"));
            using var vm = new WorkCountdownViewModel(() => now, new Memory(), false);
            vm.OvertimeText = overtime;
            int confirmations = 0;
            using var bridge = new AttendanceCountdownBridge(store, vm, _ => { confirmations++; return true; }, false, () => now, () => true);

            bridge.Tick();
            Assert.Equal("08:42", vm.StartTimeText);
            Assert.Equal(overtime, vm.OvertimeText);
            Assert.Equal(starts, vm.Schedule is not null);
            if (starts)
            {
                Assert.Null(vm.Schedule!.NormalEnd);
                Assert.Equal(at, store.LoadOptions().HandledClockIn);
            }
            else
            {
                Assert.Contains("4", vm.ErrorMessage);
                Assert.Null(store.LoadOptions().HandledClockIn);
            }
            int first = confirmations;
            bridge.Tick();
            Assert.Equal(first, confirmations);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void WeekendPrefillCanBeRecoveredAfterReopeningBeforeSettingAPlan()
    {
        string root = Path.Combine(Path.GetTempPath(), "ToolsBox-weekend-reopen-" + Guid.NewGuid().ToString("N"));
        DateTime now = new(2026, 10, 24, 9, 0, 0), at = now.AddMinutes(-18);
        var store = new AttendanceStore(root);
        try
        {
            store.Update(o => o with { Enabled = true });
            store.SaveSnapshot(new(now, at, "", "synthetic"));
            for (int i = 0; i < 2; i++)
            {
                using var vm = new WorkCountdownViewModel(() => now, new Memory(), false);
                using var bridge = new AttendanceCountdownBridge(store, vm, _ => true, false, () => now, () => true);
                bridge.Tick();
                Assert.Equal("08:42", vm.StartTimeText);
                Assert.Null(vm.Schedule);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Memory : ICountdownStateStore
    {
        public CountdownState? Value;
        public CountdownState? Load()=>Value;
        public void Save(CountdownState value)=>Value=value;
        public void Clear()=>Value=null;
    }
    [Fact]
    public void InvalidInputCanRetryAndExplicitDeclineIsRemembered()
    {
        string root=Path.Combine(Path.GetTempPath(),"ToolsBox-attendance-bridge-"+Guid.NewGuid().ToString("N"));
        DateTime now=new(2026,9,22,9,0,0),at=now.AddMinutes(-20);var store=new AttendanceStore(root);
        try
        {
            store.Update(o=>o with{Enabled=true});store.SaveSnapshot(new(now,at,"","synthetic"));
            using var vm=new WorkCountdownViewModel(()=>now,new Memory(),false);vm.OvertimeText="bad";
            bool accept = true;
            using var bridge=new AttendanceCountdownBridge(store,vm,_=>accept,false,()=>now,()=>true);
            bridge.Tick();Assert.Null(store.LoadOptions().HandledClockIn);
            vm.OvertimeText="0";bridge.Tick();Assert.Equal(at,store.LoadOptions().HandledClockIn);Assert.NotNull(vm.Schedule);
            accept = false;
            store.Update(o=>o with{HandledClockIn=null});store.SaveSnapshot(new(now,at.AddMinutes(1),"","synthetic"));
            bridge.Tick();Assert.Equal(at.AddMinutes(1),store.LoadOptions().HandledClockIn);Assert.Equal(at,vm.Schedule!.Start);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void DisablingDuringConfirmationDoesNotChangeCountdown()
    {
        string root=Path.Combine(Path.GetTempPath(),"ToolsBox-attendance-bridge-"+Guid.NewGuid().ToString("N"));
        DateTime now=new(2026,9,22,9,0,0);var store=new AttendanceStore(root);
        try
        {
            store.Update(o=>o with{Enabled=true});store.SaveSnapshot(new(now,now.AddMinutes(-20),"","synthetic"));
            using var vm=new WorkCountdownViewModel(()=>now,new Memory(),false);vm.StartTimeText="0800";vm.StartCommand.Execute(null);
            using var bridge=new AttendanceCountdownBridge(store,vm,_=>{store.Update(o=>o with{Enabled=false});return true;},false,()=>now,()=>true);
            bridge.Tick();Assert.Null(store.LoadOptions().HandledClockIn);Assert.Equal(now.Date.AddHours(8),vm.Schedule!.Start);
        }
        finally{Directory.Delete(root,true);}
    }
}
