using System.Windows;
using ToolsBox.Core.FileUnlocking;
using ToolsBox.Windows.FileUnlocking;
using ToolsBox.Windows.NetworkTraffic;
using ToolsBox.App.Infrastructure;
using System.Diagnostics;
using System.Security.Principal;
using System.ComponentModel;

namespace ToolsBox.App;

public partial class App : Application
{
    private Attendance.AttendanceHost? _attendanceHost;
    private ToolboxExitChannel? _exitChannel;
    private System.Threading.Timer? _exitWatcher;
    private System.Threading.Timer? _exitDeadline;
    private int _exitStarted;
    /// <returns>false：协调退出通道未初始化（如测试宿主未跑 OnStartup），由调用方回退本地退出；true：已发起协调退出或已报错中止。</returns>
    public bool ExitEntireToolbox()
    {
        if(_exitChannel is null)return false;
        try{_exitChannel.RequestExit();BeginCoordinatedExit();}
        catch(Exception){MessageBox.Show("无法通知其他工具箱进程退出，请重试。","退出失败");}
        return true;
    }
    private void BeginCoordinatedExit()
    {
        if(Interlocked.Exchange(ref _exitStarted,1)!=0)return;
        // Only terminate this process if its own normal cleanup is stuck; never enumerate/kill other apps.
        _exitDeadline=new System.Threading.Timer(_=>{using var self=Process.GetCurrentProcess();self.Kill();},null,TimeSpan.FromSeconds(10),Timeout.InfiniteTimeSpan);
        Dispatcher.BeginInvoke(()=>
        {
            // Close the main window directly: Shutdown() alone can skip OnClosed cleanup (tray icon, view-models).
            if(MainWindow is MainWindow main){main.PrepareForEntireToolboxExit();main.Close();return;}
            if(MainWindow is WebResources.WebResourceWindow browser){browser.CloseForToolboxExit();return;}
            Shutdown();
        });
    }
    private MainInstanceGate? _mainInstance;
    private System.Windows.Threading.DispatcherTimer? _activationTimer;
    protected override async void OnStartup(StartupEventArgs e)
    {
        // Helpers share this executable but must remain headless across awaits.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
        if(e.Args.Length==0 || e.Args.Contains("--administrator-start") || e.Args.Contains("--attendance-agent") || e.Args.Contains("--web-resources"))
        {
            _exitChannel=new ToolboxExitChannel();
            _exitWatcher=new System.Threading.Timer(_=>{if(_exitChannel.ShouldExit())BeginCoordinatedExit();},null,500,500);
        }

        if(e.Args.Length==1 && e.Args[0]=="--attendance-agent")
        {
            try
            {
                if(!new Attendance.AttendanceStore().LoadOptions().Enabled){Shutdown(0);return;}
                _attendanceHost=Attendance.AttendanceHost.Start();
                if(_attendanceHost is null)Shutdown(0);
            }
            catch{Shutdown(1);}
            return;
        }

        if (e.Args.Length == 3 && e.Args[0] == "--web-resources")
        {
            if (!WebResources.WebResourceLauncher.CanRunBrowser(WebResources.WebResourceLauncher.IsAdministrator()) ||
                !int.TryParse(e.Args[1], out int parentId) || !long.TryParse(e.Args[2], out long parentTicks))
            { Shutdown(1); return; }
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var browser = new WebResources.WebResourceWindow();
            MainWindow = browser;
            browser.Show();
            browser.WatchParent(parentId, parentTicks);
            return;
        }

        if (e.Args.Length == 1 && e.Args[0] == "--file-path-worker")
        {
            try
            {
                await Task.Run(FilePathQueryWorker.Run);
                Shutdown(0);
            }
            catch { Shutdown(1); }
            return;
        }

        if (e.Args.Length == 3 && string.Equals(e.Args[0], "--elevated-network", StringComparison.Ordinal))
        {
            int exitCode;
            try
            {
                exitCode = await ElevatedNetworkHelper.RunAsync(e.Args[1], e.Args[2]);
            }
            catch
            {
                exitCode = 1;
            }

            Shutdown(exitCode);
            return;
        }

        if (e.Args.Length == 2 && string.Equals(e.Args[0], "--elevated-unlock", StringComparison.Ordinal))
        {
            FileUnlockResult result;
            try
            {
                result = await WindowsFileLockService.ExecuteElevatedRequestAsync(e.Args[1]);
            }
            catch
            {
                result = FileUnlockResult.Failure("管理员操作请求无效。");
            }

            Shutdown(result.Succeeded ? 0 : 1);
            return;
        }

        if (e.Args.Length > 0 && e.Args[0].StartsWith("--elevated-", StringComparison.Ordinal))
        {
            Shutdown(1);
            return;
        }

        using var identity = WindowsIdentity.GetCurrent();
        bool administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        try
        {
            if (!administrator && e.Args.Contains("--administrator-start"))
                throw new InvalidOperationException("未能取得管理员权限。");
            if (!AdministratorStartup.EnsureAdministrator(administrator,
                    info => { using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动管理员进程。"); },
                    Environment.ProcessPath!, Environment.GetCommandLineArgs().FirstOrDefault()))
            {
                Shutdown(0);
                return;
            }
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            Shutdown(0); // User declined UAC; never open a non-elevated main window.
            return;
        }
        catch
        {
            MessageBox.Show("启动需要管理员权限，但未能完成授权。请右键以管理员身份运行。", "宝哥工具箱", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        try
        {
            _mainInstance=MainInstanceGate.Acquire("Local\\ToolsBox.MainWindow."+identity.User!.Value);
            if(_mainInstance is null){Shutdown(0);return;}
        }
        catch
        {
            MessageBox.Show("无法确认工具箱是否已运行，请先退出已有工具箱后重试。","宝哥工具箱");
            Shutdown(1);return;
        }
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow = new MainWindow();
        MainWindow.Show();
        _activationTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};
        _activationTimer.Tick+=(_,_)=>
        {
            if(_mainInstance?.ConsumeActivation()!=true || MainWindow is null)return;
            MainWindow.Show();MainWindow.WindowState=WindowState.Normal;MainWindow.Activate();
        };
        _activationTimer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exitWatcher?.Dispose();
        _activationTimer?.Stop();_mainInstance?.Dispose();_attendanceHost?.Dispose();base.OnExit(e);
    }
}
