using System.IO;
using ToolsBox.App.Attendance;
using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Tests.Attendance;

public sealed class AttendanceBridgeTests
{
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
