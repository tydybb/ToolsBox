using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ToolsBox.App.Tests.ArchiveRecovery;

public sealed class ArchiveWorkerProcessTests
{
    [Fact]
    public void RestrictedWorker_IsOwnedAndReapedOnDispose()
    {
        Type? type = typeof(ToolsBox.Windows.FileUnlocking.WindowsFileLockService).Assembly
            .GetType("ToolsBox.Windows.ArchiveRecovery.ArchiveWorkerProcess");
        Assert.NotNull(type);
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = type!.GetMethod("Start", BindingFlags.Public | BindingFlags.Static)!;
        using var worker = (IDisposable)start.Invoke(null, [executable, new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 60" }])!;
        int id = (int)type.GetProperty("Id")!.GetValue(worker)!;
        using var observer = Process.GetProcessById(id);
        Assert.False(observer.HasExited);
        Assert.True(OpenProcessToken(observer.Handle, 10 /* QUERY | DUPLICATE */, out var token));
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            Assert.False(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
        worker.Dispose();
        Assert.True(observer.WaitForExit(5000));
    }
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
}
