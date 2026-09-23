using System.IO;
using ToolsBox.App.Attendance;
using ToolsBox.Core.Attendance;

namespace ToolsBox.App.Tests.Attendance;

public sealed class AttendanceCoordinatorTests
{
    [Theory]
    [InlineData(8, true)]
    [InlineData(19, false)]
    public async Task ExplicitRequestReadsDespiteConfirmationOrEvening(int hour, bool confirmed)
    {
        string root=Path.Combine(Path.GetTempPath(),"ToolsBox-attendance-test-"+Guid.NewGuid().ToString("N"));
        var store=new AttendanceStore(root);
        try
        {
            DateTime now=new(2026,9,22,hour,0,0);
            store.Update(o=>o with{Enabled=true,CheckRequestedAt=now,ManualConfirmedOn=confirmed?DateOnly.FromDateTime(now):null});
            int reads=0,alerts=0;
            var c=new AttendanceCoordinator(store,(_,_)=>{reads++;return Task.FromResult(new AttendanceStatus(true,true,"07:50",null,1,null));},()=>now);
            c.ReminderDue+=(_,_)=>alerts++;
            await c.CheckAsync(true,true);
            Assert.Equal(1,reads);Assert.Equal(0,alerts);
            Assert.Equal(now.Date.AddHours(7).AddMinutes(50),store.LoadSnapshot()!.ClockInAt);
            Assert.Equal(confirmed?DateOnly.FromDateTime(now):null,store.LoadOptions().ManualConfirmedOn);
            await c.CheckAsync(true,true);
            Assert.Equal(1,reads);
            now=now.AddSeconds(1);
            store.Update(o=>o with{CheckRequestedAt=now,OverrideDate=DateOnly.FromDateTime(now),OverrideWorkday=false});
            await c.CheckAsync(true,true);
            Assert.Equal(2,reads);Assert.Equal(0,alerts);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task FinalReminderStillFiresWhilePreviousReadIsStuck()
    {
        string root=Path.Combine(Path.GetTempPath(),"ToolsBox-attendance-test-"+Guid.NewGuid().ToString("N"));
        var store=new AttendanceStore(root);
        try
        {
            DateTime now=new(2026,9,22,9,24,59);store.Update(o=>o with{Enabled=true});
            var pending=new TaskCompletionSource<AttendanceStatus>();
            var c=new AttendanceCoordinator(store,(_,_)=>pending.Task,()=>now);
            int alerts=0;c.ReminderDue+=(_,_)=>alerts++;
            var reading=c.CheckAsync(true,true);now=now.AddSeconds(1);
            await c.CheckAsync(true);Assert.Equal(1,alerts);
            now=now.AddMinutes(1);pending.SetResult(AttendanceStatus.Failure("busy"));await reading;
            Assert.Equal(1,alerts);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task DisablingWhileReadIsPendingDiscardsResultAndPopup()
    {
        string root = Path.Combine(Path.GetTempPath(),"ToolsBox-attendance-test-"+Guid.NewGuid().ToString("N"));
        var store = new AttendanceStore(root);
        try
        {
            store.Update(o=>o with { Enabled=true });
            var pending = new TaskCompletionSource<AttendanceStatus>();
            DateTime now = new(2026,9,22,8,0,0);
            var coordinator = new AttendanceCoordinator(store,(_,_)=>pending.Task,()=>now);
            int alerts=0; coordinator.ReminderDue += (_,_)=>alerts++;
            Task check = coordinator.CheckAsync(true,true);
            store.Update(o=>o with { Enabled=false });
            pending.SetResult(new(true,true,"07:50",null,1,null));
            await check;
            Assert.Null(store.LoadSnapshot()); Assert.Equal(0,alerts);
        }
        finally { Directory.Delete(root,true); }
    }
    [Fact]
    public async Task UnknownRemindsButRealClockInSuppressesAndManualNeverMakesTime()
    {
        string root = Path.Combine(Path.GetTempPath(),"ToolsBox-attendance-test-"+Guid.NewGuid().ToString("N"));
        var store = new AttendanceStore(root);
        try
        {
            DateTime now = new(2026,9,22,8,0,0);
            store.Update(o=>o with { Enabled=true });
            var result = AttendanceStatus.Failure("unsupported");
            var c = new AttendanceCoordinator(store,(_,_)=>Task.FromResult(result),()=>now);
            int alerts=0;c.ReminderDue+=(_,_)=>alerts++;
            await c.CheckAsync(true,true); Assert.Equal(1,alerts); Assert.Null(store.LoadSnapshot()!.ClockInAt);
            store.Update(o=>o with {ManualConfirmedOn=DateOnly.FromDateTime(now)});
            await c.CheckAsync(true,true); Assert.Equal(1,alerts); Assert.Null(store.LoadSnapshot()!.ClockInAt);
            store.Update(o=>o with {ManualConfirmedOn=null}); now=now.AddMinutes(5);
            result=new(true,true,"07:51",null,1,null);
            await c.CheckAsync(true,true);
            Assert.Equal(now.Date.AddHours(7).AddMinutes(51),store.LoadSnapshot()!.ClockInAt);Assert.Equal(1,alerts);
        }
        finally { Directory.Delete(root,true); }
    }
}
