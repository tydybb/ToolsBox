using System.Diagnostics;
using System.IO;
using ToolsBox.Core.FileUnlocking;
using ToolsBox.Windows.FileUnlocking;

namespace ToolsBox.App.Tests.Startup;

public sealed class SystemHandleScannerTests
{
    [Fact]
    public async Task FindLocksAsync_DiscoversCurrentProcessFileHandle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string path = Path.Combine(Path.GetTempPath(), $"ToolsBox-lock-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(path, "locked");
        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var service = CreateService();

            IReadOnlyList<FileLockEntry> entries = await service.FindLocksAsync(FileLockTarget.FromExistingPath(path));

            Assert.Contains(entries, entry => entry.ProcessId == Environment.ProcessId &&
                                              string.Equals(entry.LockedPath, path, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string path = Path.Combine(Path.GetTempPath(), $"ToolsBox-close-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(path, "locked");
        using Process holder = StartLockHolder(path);
        try
        {
            Assert.Equal("READY", await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            var service = CreateService();
            IReadOnlyList<FileLockEntry> entries = await service.FindLocksAsync(FileLockTarget.FromExistingPath(path));
            FileLockEntry entry = Assert.Single(entries.Where(item => item.ProcessId == holder.Id));

            FileUnlockResult result = await service.CloseHandleAsync(entry);

            Assert.True(result.Succeeded, result.Message);
            Assert.False(holder.HasExited);
            await using FileStream reopened = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            if (!holder.HasExited)
            {
                await holder.StandardInput.WriteLineAsync();
                await holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }

            File.Delete(path);
        }
    }

    private static Process StartLockHolder(string path)
    {
        const string script = "$stream = [System.IO.File]::Open($env:TOOLSBOX_LOCK_TEST, 'Open', 'ReadWrite', 'None'); " +
                              "[Console]::WriteLine('READY'); [Console]::Out.Flush(); " +
                              "[Console]::ReadLine() | Out-Null; $stream.Dispose()";
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);
        startInfo.Environment["TOOLSBOX_LOCK_TEST"] = path;
        return Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动测试锁定进程。");
    }

    private static WindowsFileLockService CreateService() => new(
        Environment.GetEnvironmentVariable("TOOLSBOX_TEST_WORKER") ?? Path.Combine(AppContext.BaseDirectory, "宝哥工具箱.dll"));
}
