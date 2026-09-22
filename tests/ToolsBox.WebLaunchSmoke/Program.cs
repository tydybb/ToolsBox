using System.ComponentModel;
using System.IO;
using ToolsBox.App.WebResources;

var report = Path.Combine(AppContext.BaseDirectory, "launch-result.txt");
if (args.Length == 3 && args[0] == "--web-resources")
{
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "child-result.txt"), WebResourceLauncher.IsAdministrator() ? "FAIL elevated child" : "PASS ordinary child");
    return WebResourceLauncher.IsAdministrator() ? 1 : 0;
}
try
{
    if (!WebResourceLauncher.IsAdministrator()) { File.WriteAllText(report, "SKIP parent is not elevated"); return 2; }
    using var child = WebResourceLauncher.Launch();
    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
    File.WriteAllText(report, "Parent elevated; child exit=" + child.ExitCode);
    return child.ExitCode;
}
catch (Exception error)
{
    File.WriteAllText(report, error + (error is Win32Exception w ? "\nNativeErrorCode=" + w.NativeErrorCode : ""));
    return 1;
}
