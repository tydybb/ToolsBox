using System.Diagnostics;
using System.Globalization;
using System.IO;
using ToolsBox.Core.Attendance;

namespace ToolsBox.App.Attendance;

public sealed record AttendancePreviewRequest(Guid Id,string Directory,DateTime RequestedAt);
public sealed record AttendancePreviewResult(Guid Id,string Directory,DateTime CheckedAt,DateTime? ClockInAt,string Message);

/// <summary>Isolated preview channel; never writes options or the active attendance snapshot.</summary>
public sealed class AttendancePreviewWorker(AttendanceStore store,Func<string,DateTime,Task<AttendanceStatus>> read,Func<DateTime>? clock=null)
{
    private bool _busy;
    private Guid? _handled;
    private readonly Func<DateTime> _now=clock??(()=>DateTime.Now);
    public async Task TickAsync()
    {
        if(_busy || !store.LoadOptions().Enabled)return;
        var request=store.LoadPreviewRequest();var now=_now();
        if(request is null || request.Id==_handled || request.RequestedAt.Date!=now.Date || now-request.RequestedAt>TimeSpan.FromMinutes(2) || request.RequestedAt>now || !Path.IsPathFullyQualified(request.Directory))return;
        _busy=true;_handled=request.Id;
        try
        {
            AttendanceStatus status;
            try{status=await read(request.Directory,now);}
            catch{status=AttendanceStatus.Failure("本地读取失败，请检查目录权限或钉钉版本。");}
            var completed=_now();
            if(!store.LoadOptions().Enabled || completed.Date!=now.Date || store.LoadPreviewRequest()?.Id!=request.Id)return;
            DateTime? at=null;
            if(status.Obtained && status.ClockedIn && TimeOnly.TryParseExact(status.ClockInTime,["HH:mm","H:mm","HH:mm:ss"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var time))
            {var candidate=now.Date.Add(time.ToTimeSpan());if(candidate<=completed)at=candidate;}
            store.SavePreviewResult(new(request.Id,request.Directory,completed,at,
                at is DateTime found?$"今日上班打卡：{found:HH:mm}（辅助识别）":"尚未确认今日打卡。"+(status.Error??"请在钉钉核对。")));
        }
        finally{_busy=false;}
    }
}

public static class AttendancePreview
{
    public static async Task<AttendancePreviewResult> CheckAsync(AttendanceStore store,string directory,Action launch,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!store.LoadOptions().Enabled)throw new InvalidOperationException("请先启用打卡提醒并同意本地读取。");
        var request=new AttendancePreviewRequest(Guid.NewGuid(),directory,DateTime.Now);
        store.SavePreviewRequest(request);launch();
        var elapsed=Stopwatch.StartNew();
        while(elapsed.Elapsed<TimeSpan.FromSeconds(60))
        {
            token.ThrowIfCancellationRequested();
            if(!store.LoadOptions().Enabled)throw new InvalidOperationException("打卡提醒已关闭，本次检查停止。");
            if(store.LoadPreviewRequest()?.Id!=request.Id)throw new InvalidOperationException("另一窗口发起了检查，请重试。");
            var result=store.LoadPreviewResult();
            if(result?.Id==request.Id && result.Directory==directory)return result;
            await Task.Delay(250,token);
        }
        throw new TimeoutException("检查超时，请确认新版打卡后台正在运行，再重试。");
    }

    public static void Select(AttendanceStore store,string directory,AttendancePreviewResult? result,DateTime now)
    {
        if(!Path.IsPathFullyQualified(directory)||!Directory.Exists(directory))throw new IOException("目录不存在，请重新识别。");
        bool enabled=store.LoadOptions().Enabled;
        bool valid=enabled && result?.Directory==directory && result.CheckedAt.Date==now.Date && result.CheckedAt<=now &&
            result.ClockInAt is DateTime at && at.Date==now.Date && at<=now;
        store.Update(o=>o with{AccountDirectory=directory,HandledClockIn=o.AccountDirectory==directory?o.HandledClockIn:null,CheckRequestedAt=null});
        if(valid)
        {
            var old=store.LoadSnapshot();
            store.SaveSnapshot(new(result!.CheckedAt,result.ClockInAt,directory,result.Message,old?.LastReminder,old?.FinalReminderDate));
        }
    }
}
