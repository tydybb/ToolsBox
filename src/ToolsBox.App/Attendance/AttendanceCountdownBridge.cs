using System.Windows;
using System.Windows.Threading;
using System.Security.Principal;
using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App.Attendance;

/// <summary>One live consumer at a time: main window first, otherwise the ordinary reminder host.</summary>
public sealed class AttendanceCountdownBridge : IDisposable
{
    private readonly AttendanceStore _store;
    private readonly WorkCountdownViewModel _countdown;
    private readonly Func<DateTime,bool> _confirm;
    private readonly DispatcherTimer? _timer;
    private bool _handling;
    private readonly Func<DateTime> _now;
    private readonly Func<bool> _canConsume;
    private static bool _processHandling;
    public AttendanceCountdownBridge(AttendanceStore store,WorkCountdownViewModel countdown,Func<DateTime,bool> confirm,bool startTimer=true,Func<DateTime>? now=null,Func<bool>? canConsume=null)
    {
        _store=store;_countdown=countdown;_confirm=confirm;
        _now=now??(()=>DateTime.Now);_canConsume=canConsume??AttendanceHost.IsInteractiveDesktop;
        if(startTimer){_timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};_timer.Tick+=OnTick;_timer.Start();}
    }
    public void Tick()
    {
        if(_handling || _processHandling || !_canConsume())return;
        using var identity = WindowsIdentity.GetCurrent();
        using var mutex=new Mutex(false,"Local\\ToolsBox.Attendance.Import."+identity.User!.Value);
        bool acquired;
        try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
        if(!acquired)return;
        _handling=true;
        _processHandling=true;
        try
        {
            var options=_store.LoadOptions();var snapshot=_store.LoadSnapshot();DateTime now=_now();
            if(!options.Enabled || !options.IsWorkday(DateOnly.FromDateTime(now)) || !AttendanceCoordinator.IsConfirmed(snapshot,options,now) ||
                snapshot!.ClockInAt==options.HandledClockIn)return;
            var at=snapshot.ClockInAt!.Value;bool declined=false;
            CountdownDayMode? mode=options.OverrideDate==DateOnly.FromDateTime(now)&&options.OverrideWorkday==true?CountdownDayMode.Workday:null;
            bool imported=_countdown.ImportClockIn(at,()=>
            {
                bool answer=_confirm(at);
                var current=_store.LoadOptions();
                if(!current.Enabled || current.AccountDirectory!=options.AccountDirectory || current.OverrideDate!=options.OverrideDate || current.OverrideWorkday!=options.OverrideWorkday || !_canConsume() || _now().Date!=now.Date)return false;
                declined=!answer;return answer;
            },mode);
            if(imported || declined)
                _store.Update(o=>o.Enabled && o.AccountDirectory==options.AccountDirectory ? o with{HandledClockIn=at}:o);
        }
        finally{_handling=false;_processHandling=false;mutex.ReleaseMutex();}
    }
    private void OnTick(object? sender,EventArgs e)
    {
        try{Tick();}catch(Exception){ /* Settings stay untouched; no silent enable or false confirmation. */ }
    }
    public static bool Confirm(DateTime at) => MessageBox.Show(
        $"读取到今日上班打卡时间 {at:HH:mm}，与当前倒计时或输入不同。\n\n是否按打卡时间更新？当前加班设置保留。",
        "更新下班倒计时",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes;
    public void Dispose(){if(_timer is not null){_timer.Stop();_timer.Tick-=OnTick;}}
}
