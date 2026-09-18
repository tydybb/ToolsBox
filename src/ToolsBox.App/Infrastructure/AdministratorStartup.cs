using System.Diagnostics;
using System.IO;

namespace ToolsBox.App.Infrastructure;

public static class AdministratorStartup
{
    // The executable also hosts the restricted archive worker. Elevate only the main UI path.
    public static bool EnsureAdministrator(bool isAdministrator, Action<ProcessStartInfo> launch, string executable, string? entryAssembly)
    {
        if (isAdministrator) return true;
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(entryAssembly)) throw new InvalidOperationException("缺少应用入口路径。");
            start.ArgumentList.Add(Path.GetFullPath(entryAssembly));
        }
        start.ArgumentList.Add("--administrator-start");
        launch(start);
        return false;
    }
}
