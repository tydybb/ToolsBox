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
    protected override async void OnStartup(StartupEventArgs e)
    {
        // Helpers share this executable but must remain headless across awaits.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

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

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
