using System.Diagnostics;
using System.Text.Json;

namespace ToolsBox.Windows.FileUnlocking;

// Serial, process-isolated native queries: a stuck driver cannot trap the scanner thread.
internal sealed class FilePathQueryClient(string executable, Func<ProcessStartInfo, Process>? startProcess = null) : IDisposable
{
    private Process? _worker;
    public int SkippedHandleCount { get; private set; }

    public string? GetPath(IntPtr handle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureStarted(cancellationToken);
        Process worker = _worker!;
        try
        {
            // Transfer our duplicate, not the original process's potentially reused handle.
            if (!FileHandleNativeMethods.DuplicateHandle(FileHandleNativeMethods.GetCurrentProcess(), handle,
                    worker.Handle, out IntPtr remote, 0, false, FileHandleNativeMethods.DuplicateSameAccess))
                return null;
            worker.StandardInput.WriteLine(remote.ToInt64());
            worker.StandardInput.Flush();
            string? response = worker.StandardOutput.ReadLineAsync(cancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(250), cancellationToken).GetAwaiter().GetResult();
            if (response is null) throw new IOException("文件查询辅助进程意外退出。");
            return JsonSerializer.Deserialize<string?>(response);
        }
        catch (TimeoutException)
        {
            StopWorker();
            SkippedHandleCount++;
            return null;
        }
        catch
        {
            StopWorker();
            throw;
        }
    }

    private void EnsureStarted(CancellationToken cancellationToken)
    {
        if (_worker is not null) return;
        bool managedAssembly = Path.GetExtension(executable).Equals(".dll", StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo(managedAssembly ? "dotnet" : executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        };
        if (managedAssembly) start.ArgumentList.Add(executable);
        start.ArgumentList.Add("--file-path-worker");
        try
        {
            _worker = startProcess is null
                ? Process.Start(start) ?? throw new IOException("无法启动文件查询辅助进程。")
                : startProcess(start);
            string? ready = _worker.StandardOutput.ReadLineAsync(cancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).GetAwaiter().GetResult();
            if (ready != "ToolsBox.FilePathQuery.v1") throw new IOException("文件查询辅助进程初始化失败。");
        }
        catch
        {
            StopWorker();
            throw;
        }
    }

    private void StopWorker()
    {
        Process? worker = _worker;
        _worker = null;
        if (worker is null) return;
        try
        {
            // Only terminate the helper we created; never terminate the owner of a scanned handle.
            if (!worker.HasExited) worker.Kill();
            if (!worker.WaitForExit(2000)) throw new IOException("文件查询辅助进程未能退出，已停止扫描。");
        }
        finally { worker.Dispose(); }
    }

    public void Dispose() => StopWorker();
}
