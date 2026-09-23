using System.IO;
using ToolsBox.App.Attendance;
using ToolsBox.Core.Attendance;

namespace ToolsBox.App.Tests.Attendance;

public sealed class AttendancePreviewTests
{
    [Fact]
    public async Task ClientRequestRoundTripsThroughWorkerWithoutChangingOptions()
    {
        string root=Path.Combine(Path.GetTempPath(),"ToolsBox-preview-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new AttendanceStore(root);store.Update(o=>o with{Enabled=true,AccountDirectory="original"});
            var worker=new AttendancePreviewWorker(store,(_,_)=>Task.FromResult(AttendanceStatus.Failure("synthetic unsupported")));
            Task? work=null;
            var check=AttendancePreview.CheckAsync(store,root,()=>work=worker.TickAsync(),CancellationToken.None);
            await work!;var result=await check;
            Assert.Equal(root,result.Directory);Assert.Contains("synthetic unsupported",result.Message);
            Assert.Null(store.LoadSnapshot());Assert.Equal("original",store.LoadOptions().AccountDirectory);
            using var canceled=new CancellationTokenSource();canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>AttendancePreview.CheckAsync(store,root,()=>{},canceled.Token));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task PreviewDoesNotChangeSelectedAccountUntilExplicitSelection()
    {
        string root=Path.Combine(Path.GetTempPath(),"ToolsBox-preview-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store=new AttendanceStore(Path.Combine(root,"settings"));DateTime now=new(2026,9,23,10,0,0);
            store.Update(o=>o with{Enabled=true,AccountDirectory="original"});
            var request=new AttendancePreviewRequest(Guid.NewGuid(),root,now);store.SavePreviewRequest(request);
            int reads=0;
            var worker=new AttendancePreviewWorker(store,(_,_)=>{reads++;return Task.FromResult(new AttendanceStatus(true,true,"07:50",null,1,null));},()=>now);
            await worker.TickAsync();await worker.TickAsync();
            Assert.Equal(1,reads);Assert.Equal("original",store.LoadOptions().AccountDirectory);Assert.Null(store.LoadSnapshot());
            var result=store.LoadPreviewResult();Assert.Equal(request.Id,result!.Id);Assert.Equal(now.Date.AddHours(7).AddMinutes(50),result.ClockInAt);
            AttendancePreview.Select(store,root,result,now);
            Assert.Equal(root,store.LoadOptions().AccountDirectory);Assert.Equal(result.ClockInAt,store.LoadSnapshot()!.ClockInAt);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task ChangedRequestAndDisableDiscardPendingResult()
    {
        string root=Path.Combine(Path.GetTempPath(),"ToolsBox-preview-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new AttendanceStore(root);DateTime now=new(2026,9,23,10,0,0);
            store.Update(o=>o with{Enabled=true});
            var pending=new TaskCompletionSource<AttendanceStatus>();
            store.SavePreviewRequest(new(Guid.NewGuid(),root,now));
            var worker=new AttendancePreviewWorker(store,(_,_)=>pending.Task,()=>now);
            var read=worker.TickAsync();store.SavePreviewRequest(new(Guid.NewGuid(),root,now));
            pending.SetResult(new(true,true,"08:00",null,1,null));await read;
            Assert.Null(store.LoadPreviewResult());
            store.Update(o=>o with{Enabled=false});await worker.TickAsync();Assert.Null(store.LoadPreviewResult());
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void OldOrDifferentDirectoryResultsAreNotImported()
    {
        string root=Path.Combine(Path.GetTempPath(),"ToolsBox-preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var store=new AttendanceStore(Path.Combine(root,"settings"));DateTime now=new(2026,9,23,10,0,0);store.Update(o=>o with{Enabled=true});
            AttendancePreview.Select(store,root,new(Guid.NewGuid(),root,now.AddDays(-1),now.AddDays(-1),"old"),now);Assert.Null(store.LoadSnapshot());
            AttendancePreview.Select(store,root,new(Guid.NewGuid(),root+"other",now,now,"wrong"),now);Assert.Null(store.LoadSnapshot());
        }
        finally{Directory.Delete(root,true);}
    }
}
