using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using ToolsBox.App.Infrastructure;
using ToolsBox.App.Views;
using ToolsBox.App.WorkCountdown;
using ToolsBox.Core.Attendance;

namespace ToolsBox.App.Attendance;

public sealed class AttendanceHost : IDisposable
{
    private readonly AttendanceStore _store = new();
    private readonly Mutex _instance;
    private readonly AttendanceCoordinator _coordinator;
    private readonly AttendancePreviewWorker _preview;
    private readonly DispatcherTimer _timer;
    private readonly TrayIcon _tray;
    private AttendanceReminderWindow? _reminder;
    private Window? _settings;
    private WorkCountdownViewModel? _countdown;
    private AttendanceCountdownBridge? _bridge;
    private OffWorkReminderWindow? _offWork;
    private DateTime? _request;
    private bool _force=true,_interactive,_disposed,_suspended;
    public static string MainPresenceName => "Local\\ToolsBox.Attendance.Main."+WindowsIdentity.GetCurrent().User!.Value;
    private static string InstanceName => "Local\\ToolsBox.Attendance.Agent."+WindowsIdentity.GetCurrent().User!.Value;

    private AttendanceHost(Mutex instance)
    {
        _instance=instance;
        _preview=new AttendancePreviewWorker(_store,(directory,now)=>Task.Run(()=>new DingTalkAttendanceChecker(new DingTalkDataLocator([directory])).Check(now)));
        _coordinator=new AttendanceCoordinator(_store,(options,now)=>Task.Run(()=>
            new DingTalkAttendanceChecker(string.IsNullOrWhiteSpace(options.AccountDirectory)?null:new DingTalkDataLocator([options.AccountDirectory])).Check(now)));
        _coordinator.ReminderDue+=OnReminder;
        _tray=new TrayIcon(ShowSettings,()=>{if(Application.Current is App app && app.ExitEntireToolbox())return;Application.Current.Shutdown();},visible:!HasMainConsumer());
        _timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};_timer.Tick+=OnTick;
        SystemEvents.SessionSwitch+=OnSession;SystemEvents.PowerModeChanged+=OnPower;
        _timer.Start();
    }
    public static AttendanceHost? Start()
    {
        // Never decrypt user messaging data with administrator privileges.
        if(WebResources.WebResourceLauncher.IsAdministrator())return null;
        var mutex=new Mutex(true,InstanceName,out bool created);
        if(!created){mutex.Dispose();return null;}
        try{return new AttendanceHost(mutex);}catch{mutex.ReleaseMutex();mutex.Dispose();throw;}
    }
    private async void OnTick(object? sender,EventArgs e)
    {
        if(_disposed)return;
        try
        {
            // Update before awaiting the reader: a slow read must not leave duplicate tray icons.
            _tray.SetVisible(!HasMainConsumer());
            var options=_store.LoadOptions();
            if(!options.Enabled){_reminder?.Close();Application.Current.Shutdown();return;}
            // A malformed preview request must never disable the regular reminder loop.
            try{if(!_suspended&&IsInteractiveDesktop())await _preview.TickAsync();}catch{ }
            if(_disposed)return;
            bool interactive=!_suspended&&IsInteractiveDesktop();
            if(interactive&&!_interactive)_force=true;_interactive=interactive;
            if(!interactive || options.ManualConfirmedOn==DateOnly.FromDateTime(DateTime.Now) ||
               !options.IsWorkday(DateOnly.FromDateTime(DateTime.Now)) || AttendanceCoordinator.IsConfirmed(_store.LoadSnapshot(),options,DateTime.Now))_reminder?.Close();
            bool force=_force || _request!=options.CheckRequestedAt;_force=false;_request=options.CheckRequestedAt;
            await _coordinator.CheckAsync(interactive,force,()=>!_disposed&&!_suspended&&IsInteractiveDesktop());
            if(_disposed)return;
            if(HasMainConsumer()){CloseBackground();return;}
            if(_countdown is null)
            {
                _countdown=new WorkCountdownViewModel();
                _countdown.OffWorkReached+=(_,_)=>
                {
                    if(_disposed || HasMainConsumer() || _countdown?.Schedule?.Start.Date!=DateTime.Today)return;
                    _offWork?.Close();_offWork=new OffWorkReminderWindow(_countdown);_offWork.Show();
                };
                _bridge=new AttendanceCountdownBridge(_store,_countdown,AttendanceCountdownBridge.Confirm,false,canConsume:()=>!_disposed&&!HasMainConsumer()&&IsInteractiveDesktop());
            }
            if(interactive)_bridge?.Tick();
        }
        catch(Exception)
        {
            _reminder?.Close(); // No polling loop popups on corrupt settings or unavailable storage.
        }
    }
    private void OnReminder(object? sender,EventArgs e)
    {
        if(_disposed || !IsInteractiveDesktop())return;
        if(_reminder is null)
        {
            var window=new AttendanceReminderWindow(()=>_store.Update(o=>o with{ManualConfirmedOn=DateOnly.FromDateTime(DateTime.Now)}));
            window.Closed+=(_,_)=>{if(ReferenceEquals(_reminder,window))_reminder=null;};_reminder=window;
        }
        _reminder.Update(_store.LoadSnapshot()?.Message??"尚未确认今日打卡，请检查钉钉。",DateTime.Now.Hour==9&&DateTime.Now.Minute==25);
        if(!_reminder.IsVisible)_reminder.Show();
    }
    private void ShowSettings()
    {
        if(_settings is not null){_settings.Show();_settings.Activate();return;}
        _settings=new Window{Title="宝哥工具箱 · 打卡提醒设置（普通权限）",Width=720,Height=430,Content=new AttendancePanel(),WindowStartupLocation=WindowStartupLocation.CenterScreen};
        _settings.Closed+=(_,_)=>_settings=null;_settings.Show();
    }
    public static bool HasMainConsumer()
    {
        try{if(Mutex.TryOpenExisting(MainPresenceName,out var mutex)){mutex.Dispose();return true;}}
        catch(UnauthorizedAccessException){return true;}
        return false;
    }
    private void OnSession(object sender,SessionSwitchEventArgs e) => Application.Current.Dispatcher.BeginInvoke(()=>
    { if(_disposed)return;_force=true;if(e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect)_reminder?.Close(); });
    private void OnPower(object sender,PowerModeChangedEventArgs e) => Application.Current.Dispatcher.BeginInvoke(()=>
    { if(_disposed)return;_force=true;if(e.Mode==PowerModes.Suspend){_suspended=true;_reminder?.Close();}else if(e.Mode==PowerModes.Resume)_suspended=false; });
    private void CloseBackground(){_bridge?.Dispose();_bridge=null;_countdown?.Dispose();_countdown=null;_offWork?.Close();_offWork=null;}
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_timer.Stop();_timer.Tick-=OnTick;
        SystemEvents.SessionSwitch-=OnSession;SystemEvents.PowerModeChanged-=OnPower;
        _coordinator.ReminderDue-=OnReminder;_reminder?.Close();_settings?.Close();CloseBackground();_tray.Dispose();
        _instance.ReleaseMutex();_instance.Dispose();
    }
    internal static bool IsInteractiveDesktop()
    {
        if(!WTSQuerySessionInformation(IntPtr.Zero,-1,8,out var buffer,out int size))return false;
        try{if(size<4 || Marshal.ReadInt32(buffer)!=0)return false;}finally{WTSFreeMemory(buffer);}
        IntPtr desktop=OpenInputDesktop(0,false,1);
        if(desktop==IntPtr.Zero)return false;
        try{var name=new StringBuilder(256);return GetUserObjectInformation(desktop,2,name,name.Capacity*2,out _)&&name.ToString()=="Default";}
        finally{CloseDesktop(desktop);}
    }
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder info,int length,out int needed);
    [DllImport("user32.dll")]private static extern bool CloseDesktop(IntPtr handle);
    [DllImport("wtsapi32.dll",CharSet=CharSet.Unicode)]private static extern bool WTSQuerySessionInformation(IntPtr server,int sessionId,int info,out IntPtr buffer,out int bytes);
    [DllImport("wtsapi32.dll")]private static extern void WTSFreeMemory(IntPtr buffer);
}
