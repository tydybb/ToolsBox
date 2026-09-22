using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ToolsBox.App.Tests.Startup;

public sealed class TrayExitTests
{
    private const int WmClose = 0x0010;

    [Fact]
    public Task UserClose_HidesToTray_WhileTrayExitStillExits() =>
        WpfTestThread.RunAsync(async () =>
        {
            using var countdownModel = new ToolsBox.App.WorkCountdown.WorkCountdownViewModel(
                () => new DateTime(2026, 9, 22, 9, 0, 0), new EmptyCountdownStore(), false);
            var window = new MainWindow(countdownModel, _ => { });
            bool closed = false;
            window.Closed += (_, _) => closed = true;
            try
            {
                // EnsureHandle 会触发 SourceInitialized，装好 WM_CLOSE 钩子（与文件拖放接收器同一时机）。
                IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                // 用户点 X / Alt+F4：Win32 送达 WM_CLOSE，窗口应挂到托盘而不是退出。
                SendMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                Assert.False(closed, "WM_CLOSE 应最小化到托盘，而不是关闭窗口。");
                Assert.False(GetField(window, "_trayIcon") is null, "隐藏时应已挂载托盘图标。");

                // 托盘右键菜单“退出”入口：必须真正关闭窗口并释放图标。
                typeof(MainWindow)
                    .GetMethod("ExitFromTray", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

                Assert.True(closed, "托盘“退出”应真正关闭窗口。");
                Assert.True(GetField(window, "_trayIcon") is null, "退出后应释放托盘图标。");
            }
            finally
            {
                if (!closed) window.Close();
            }
        });

    [Fact]
    public void AppProject_DeclaresTrayExitPath_AndWinFormsForTrayIcon()
    {
        string root = FindRepositoryRoot();

        string project = File.ReadAllText(Path.Combine(root, "src", "ToolsBox.App", "ToolsBox.App.csproj"));
        Assert.Contains("<UseWindowsForms>true</UseWindowsForms>", project);

        string mainWindow = File.ReadAllText(Path.Combine(root, "src", "ToolsBox.App", "MainWindow.xaml.cs"));
        Assert.Contains("MinimizeToTray", mainWindow);
        Assert.Contains("WM_CLOSE", mainWindow);
        // 真实退出仍要确认网页资源窗口，且入口是托盘菜单。
        Assert.Contains("退出宝哥工具箱", mainWindow);
        Assert.Contains("_trayExit = true", mainWindow);

        string trayIcon = File.ReadAllText(
            Path.Combine(root, "src", "ToolsBox.App", "Infrastructure", "TrayIcon.cs"));
        Assert.Contains("NotifyIcon", trayIcon);
        Assert.Contains("退出", trayIcon);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private static object? GetField(object target, string name) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ToolsBox.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Unable to locate the ToolsBox repository root.");
    }

    private sealed class EmptyCountdownStore : ToolsBox.App.WorkCountdown.ICountdownStateStore
    {
        public ToolsBox.App.WorkCountdown.CountdownState? Load() => null;
        public void Save(ToolsBox.App.WorkCountdown.CountdownState state) { }
        public void Clear() { }
    }
}
