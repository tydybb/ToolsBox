using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ToolsBox.Core.Attendance;

namespace ToolsBox.App.Attendance;

public sealed class AttendancePanel : UserControl
{
    public const string ConsentText = "启用后会只读分析当前 Windows 用户的本地钉钉账号配置、日志和消息库，以尝试识别今日上班打卡。\n\n不上传消息，不保存明文消息库，不代替正式考勤。读取失败不能证明未打卡；钉钉版本、同步状态及数据格式会影响识别。\n\n同时添加当前用户登录自启：打开完整工具箱主窗口，需要确认 Windows 管理员授权；授权后启动普通权限打卡检测后台。拒绝授权则本次不启动。07:30 开始工作日提醒，09:25 最后一次。取消勾选会关闭本功能并移除该启动项。\n\n是否同意并启用？";
    private readonly AttendanceStore _store;
    private readonly Action<bool> _startup;
    private readonly Action _launch;
    private readonly Func<bool> _consent;
    private readonly Func<DingTalkLocateResult> _locate;
    private readonly Button _confirmed;
    private readonly TextBlock _startupStatus=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DarkOrange,Margin=new Thickness(0,6,0,6)};
    private readonly TextBlock _settingsHint=new(){Text="保存设置：保存账号数据目录及仅今天有效的工作日 / 休息日选择。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6)};
    private readonly CheckBox _enabled=new(){Content="钉钉打卡提醒（同时注册登录自启，系统状态见下方）",FontWeight=FontWeights.SemiBold};
    private readonly TextBox _path=new(){MinWidth=150,ToolTip="留空自动定位；多个账号时请直接选择本人账号目录（含 DBFiles 的目录）"};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,10,0,0)};
    private readonly ComboBox _day=new(){ItemsSource=new[]{"今日：按日历","今日：需要提醒（工作日）","今日：不提醒（休息日）"}};
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromSeconds(3)};
    public AttendancePanel() : this(new AttendanceStore(),AttendanceStartup.SetEnabled,LaunchHost,null) { }
    public AttendancePanel(AttendanceStore store,Action<bool> startup,Action launch,Func<bool>? consent,Func<DingTalkLocateResult>? locate=null)
    {
        _store=store;_startup=startup;_launch=launch;
        _locate=locate??(()=>new DingTalkDataLocator().Locate());
        _consent=consent??(()=>MessageBox.Show(ConsentText,"启用打卡提醒",MessageBoxButton.YesNo,MessageBoxImage.Information,MessageBoxResult.No)==MessageBoxResult.Yes);
        NameScope.SetNameScope(this,new NameScope());RegisterName("AttendanceEnabled",_enabled);RegisterName("AttendanceStatus",_status);
        RegisterName("AttendancePath",_path);
        var panel=new StackPanel();panel.Children.Add(_enabled);
        RegisterName("AttendanceStartupStatus",_startupStatus);panel.Children.Add(_startupStatus);
        var startupSettings=Button("打开 Windows 启动设置",()=>
        {
            using var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps"){UseShellExecute=true});
        });
        RegisterName("AttendanceStartupSettings",startupSettings);panel.Children.Add(startupSettings);
        panel.Children.Add(new TextBlock{Text="仅工作日 / 调休补班；启动、解锁、唤醒检查＋5 分钟轮询。手动确认不会生成打卡时间。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10)});
        panel.Children.Add(new TextBlock{Text="钉钉账号数据目录（留空自动定位，不是安装目录）"});
        var paths=new DockPanel{Margin=new Thickness(0,6,0,8)};
        var browse=Button("选择目录",()=>{var dialog=new OpenFolderDialog{Title="选择自己的钉钉账号数据目录"};if(dialog.ShowDialog()==true)_path.Text=dialog.FolderName;});
        var detect=Button("自动识别",()=>
        {
            var result=_locate();
            if(result.Paths is not { } found){_settingsHint.Text=result.Error??"未找到账号目录，请手动选择。";return;}
            _path.Text=found.AccountDir;
            _settingsHint.Text="已识别并填入账号目录，请点击“保存设置”应用。多账号时不会自动猜测。";
        });
        RegisterName("AttendanceDetect",detect);
        DockPanel.SetDock(browse,Dock.Right);paths.Children.Add(browse);
        DockPanel.SetDock(detect,Dock.Right);paths.Children.Add(detect);paths.Children.Add(_path);panel.Children.Add(paths);
        panel.Children.Add(_day);
        panel.Children.Add(_settingsHint);
        var actions=new WrapPanel{Margin=new Thickness(0,10,0,0)};
        actions.Children.Add(Button("保存设置",Save));
        actions.Children.Add(Button("立即检查",()=>{if(!_store.LoadOptions().Enabled)throw new InvalidOperationException("请先启用打卡提醒。");_store.Update(o=>o with{CheckRequestedAt=DateTime.Now});_launch();}));
        _confirmed=Button("我已打卡",()=>_store.Update(o=>o with{ManualConfirmedOn=DateOnly.FromDateTime(DateTime.Now)}));
        actions.Children.Add(_confirmed);
        panel.Children.Add(actions);panel.Children.Add(_status);
        Content=new Border{Child=panel,Background=Brushes.White,Padding=new Thickness(20),CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,0,16)};
        _enabled.Click+=(_,_)=>Toggle();
        _timer.Tick+=(_,_)=>RefreshStatus();
        Loaded+=(_,_)=>{Load();_timer.Start();};Unloaded+=(_,_)=>_timer.Stop();
    }
    private Button Button(string text,Action action)
    {
        var b=new Button{Content=text,Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,6,0)};
        b.Click+=(_,_)=>{try{action();RefreshStatus();}catch(Exception e){_status.Text="操作未完成："+e.Message;}};
        return b;
    }
    private static void LaunchHost(){using var process=WebResources.WebResourceLauncher.LaunchAttendance();}
    private void Load()
    {
        try{var o=_store.LoadOptions();_enabled.IsChecked=o.Enabled;_path.Text=o.AccountDirectory;
            _day.SelectedIndex=o.OverrideDate==DateOnly.FromDateTime(DateTime.Now)&&o.OverrideWorkday.HasValue?(o.OverrideWorkday.Value?1:2):0;RefreshStatus();}
        catch(Exception){_enabled.IsChecked=false;_status.Text="无法读取打卡设置，未自动启用。";}
    }
    private void Save()
    {
        string path=_path.Text.Trim().Trim('"');
        if(path.Length>0 && (!Path.IsPathFullyQualified(path)||!Directory.Exists(path)))throw new ArgumentException("请选择存在的完整数据目录，或留空自动定位。");
        _store.Update(o=>o with{AccountDirectory=path,OverrideDate=DateOnly.FromDateTime(DateTime.Now),
            OverrideWorkday=_day.SelectedIndex==0?null:_day.SelectedIndex==1,
            HandledClockIn=o.AccountDirectory==path?o.HandledClockIn:null});
        _settingsHint.Text="已保存账号目录和今日设置；今日设置不会影响明天。";
    }
    private void Toggle()
    {
        bool requested=_enabled.IsChecked==true;
        if(requested&&!_consent()){_enabled.IsChecked=false;return;}
        bool old=false;
        try
        {
            old=_store.LoadOptions().Enabled;
            _startup(requested);_store.Update(o=>o with{Enabled=requested,CheckRequestedAt=null});
            if(requested)_launch();RefreshStatus();
        }
        catch(Exception)
        {
            bool rollback=true;
            try{_store.Update(o=>o with{Enabled=old});_startup(old);}catch{rollback=false;}
            _enabled.IsChecked=old;
            _status.Text=rollback?"设置未完成，已保持原设置。请检查启动项权限与普通用户桌面是否可用。":"设置未完整恢复，请检查 Windows 启动应用中的宝哥工具箱及此勾选状态。";
        }
    }
    private void RefreshStatus()
    {
        try
        {
            var o=_store.LoadOptions();var s=_store.LoadSnapshot();var today=DateOnly.FromDateTime(DateTime.Now);
            _enabled.IsChecked=o.Enabled;
            _startupStatus.Text=o.Enabled?AttendanceStartup.ReadStatus():"登录自启：本功能未启用。";
            bool recognized=AttendanceCoordinator.IsConfirmed(s,o,DateTime.Now);
            bool manual=o.ManualConfirmedOn==today;
            _confirmed.IsEnabled=!recognized&&!manual;
            _confirmed.Content=recognized?"已打卡（已识别）":manual?"已打卡（手动确认）":"我已打卡";
            if(o.Enabled && o.CheckRequestedAt is DateTime requested && requested.Date==DateTime.Today)
            {
                if(s?.CompletedRequest!=requested)
                {
                    _status.Text=DateTime.Now-requested>TimeSpan.FromSeconds(30)
                        ? "检查尚未返回：可能正在读取或后台未响应，请确认已运行新版提醒进程后重试。"
                        : "正在请求检查今日打卡，请稍候…（手动检查全天可用）";
                    return;
                }
                if(s.AccountDirectory==o.AccountDirectory)
                {
                    _status.Text=$"{s.Message}\n最后检查：{s.CheckedAt:HH:mm:ss}" + (o.ManualConfirmedOn==today?"\n今日已手动确认，自动提醒仍保持停止。":"");
                    return;
                }
            }
            _status.Text=!o.Enabled?"未启用：不读取钉钉，不启用本功能自启。":o.ManualConfirmedOn==today?"今日已手动确认打卡，停止提醒（未生成打卡时间）。":
                !o.IsWorkday(today)?"今天不提醒；日历缺失或公司安排不同，可手动指定今日工作日。":
                s?.CheckedAt.Date==DateTime.Today&&s.AccountDirectory==o.AccountDirectory?$"{s.Message}\n最后检查：{s.CheckedAt:HH:mm:ss}" : "已启用，等待早间检查。读取失败时仍可手动确认。";
        }
        catch(Exception){_status.Text="无法读取打卡状态，请检查设置文件访问权限。";}
    }
}
