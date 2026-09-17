using System.Windows;
using ToolsBox.Core.FileUnlocking;
using ToolsBox.Windows.FileUnlocking;
using ToolsBox.Windows.NetworkTraffic;

namespace ToolsBox.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
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

        base.OnStartup(e);
    }
}
