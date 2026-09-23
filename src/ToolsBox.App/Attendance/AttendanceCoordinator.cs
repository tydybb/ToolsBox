using System.Globalization;
using ToolsBox.Core.Attendance;

namespace ToolsBox.App.Attendance;

public sealed class AttendanceCoordinator
{
    private readonly AttendanceStore _store;
    private readonly Func<AttendanceOptions,DateTime,Task<AttendanceStatus>> _read;
    private readonly Func<DateTime> _now;
    private readonly AttendancePolicy _policy = new();
    private bool _busy;
    public event EventHandler? ReminderDue;
    public AttendanceCoordinator(AttendanceStore store, Func<AttendanceOptions,DateTime,Task<AttendanceStatus>> read, Func<DateTime>? now=null)
    {
        _store=store;_read=read;_now=now??(()=>DateTime.Now);
        var snapshot=store.LoadSnapshot();
        _policy.LastReminder=snapshot?.LastReminder;
        _policy.FinalReminderDate=snapshot?.FinalReminderDate;
    }
    public async Task CheckAsync(bool unlocked,bool force=false,Func<bool>? stillUnlocked=null)
    {
        var options=_store.LoadOptions();DateTime now=_now();var today=DateOnly.FromDateTime(now);
        var previous=_store.LoadSnapshot();
        bool manual=options.CheckRequestedAt is DateTime request && request.Date==now.Date && previous?.CompletedRequest!=request;
        if(!manual && (options.ManualConfirmedOn==today || IsConfirmed(previous,options,now)))return;
        if(_busy)
        {
            if(options.Enabled && options.IsWorkday(today) && unlocked && stillUnlocked?.Invoke()!=false && now.Hour==9 && now.Minute==25 && _policy.TryRemind(now,options.ManualConfirmedOn))
            {
                _store.SaveSnapshot(new(now,null,options.AccountDirectory,"尚未确认今日打卡，读取仍在进行，请检查钉钉。",_policy.LastReminder,_policy.FinalReminderDate));
                ReminderDue?.Invoke(this,EventArgs.Empty);
            }
            return;
        }
        if(manual ? !options.Enabled || !unlocked : !_policy.TryBeginCheck(now,options.Enabled,options.IsWorkday(today),unlocked,force))return;
        _busy=true;
        try
        {
            AttendanceStatus result;
            try { result=await _read(options,now); }
            catch(Exception) { result=AttendanceStatus.Failure("本地读取失败，请检查钉钉或手动确认。"); }
            var current=_store.LoadOptions();DateTime completed=_now();
            if(!current.Enabled || current.AccountDirectory!=options.AccountDirectory || completed.Date!=now.Date ||
                (!manual && (!current.IsWorkday(today) || current.ManualConfirmedOn==today)) || stillUnlocked?.Invoke()==false)return;
            DateTime? clockIn=null;
            if(result.Obtained && result.ClockedIn && TimeOnly.TryParseExact(result.ClockInTime,["HH:mm","H:mm","HH:mm:ss"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var time))
            {
                DateTime actual=now.Date.Add(time.ToTimeSpan());
                if(actual<=completed)clockIn=actual;
            }
            bool remind=!manual && clockIn is null && _policy.TryRemind(completed,current.ManualConfirmedOn);
            string message=clockIn is DateTime at ? $"已读取本地上班打卡：{at:HH:mm}（辅助识别）" :
                "尚未确认今日打卡，请检查钉钉。" + (string.IsNullOrWhiteSpace(result.Error)?"": " " + result.Error);
            _store.SaveSnapshot(new(completed,clockIn,options.AccountDirectory,message,_policy.LastReminder,_policy.FinalReminderDate,
                manual ? options.CheckRequestedAt : previous?.CompletedRequest));
            if(remind)ReminderDue?.Invoke(this,EventArgs.Empty);
        }
        finally { _busy=false; }
    }
    public static bool IsConfirmed(AttendanceSnapshot? snapshot,AttendanceOptions options,DateTime now) =>
        snapshot?.ClockInAt is DateTime at && at.Date==now.Date && at<=now && snapshot.CheckedAt.Date==now.Date && snapshot.AccountDirectory==options.AccountDirectory;
}
