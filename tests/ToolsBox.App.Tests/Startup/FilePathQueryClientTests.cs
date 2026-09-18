using System.Diagnostics;
using System.IO;
using ToolsBox.Windows.FileUnlocking;

namespace ToolsBox.App.Tests.Startup;

public sealed class FilePathQueryClientTests
{
    [Fact]
    public void QueryTimeout_KillsOwnWorkerAndNextQueryStartsFreshWorker()
    {
        string path = Path.GetTempFileName();
        var observers = new List<Process>();
        try
        {
            using var held = File.OpenHandle(path);
            int starts = 0;
            var client = new FilePathQueryClient(Path.Combine(AppContext.BaseDirectory, "宝哥工具箱.dll"), start =>
            {
                Process worker = ++starts == 1 ? StartStalledWorker() : Process.Start(start)!;
                observers.Add(Process.GetProcessById(worker.Id));
                return worker;
            });
            using (client)
            {
                Assert.Null(client.GetPath(held.DangerousGetHandle(), CancellationToken.None));
                Assert.Equal(1, client.SkippedHandleCount);
                Assert.True(observers[0].WaitForExit(3000));
                Assert.Equal(path, client.GetPath(held.DangerousGetHandle(), CancellationToken.None));
                Assert.Equal(2, starts);
            }
            Assert.True(observers[1].WaitForExit(3000));
            // The queried source handle is still usable; only our helper is terminated.
            Assert.Equal(0, RandomAccess.GetLength(held));
        }
        finally
        {
            foreach (Process observer in observers) observer.Dispose();
            File.Delete(path);
        }
    }

    [Fact]
    public void CancellationDuringStartup_ReapsOwnedWorker()
    {
        using var cancellation = new CancellationTokenSource();
        using var held = File.OpenHandle(typeof(FilePathQueryClientTests).Assembly.Location);
        Process? observer = null;
        try
        {
            using var client = new FilePathQueryClient("unused", _ =>
            {
                Process worker = StartStalledWorker();
                observer = Process.GetProcessById(worker.Id);
                cancellation.Cancel();
                return worker;
            });
            Assert.ThrowsAny<OperationCanceledException>(() => client.GetPath(held.DangerousGetHandle(), cancellation.Token));
            Assert.NotNull(observer);
            Assert.True(observer.WaitForExit(3000));
        }
        finally { observer?.Dispose(); }
    }

    private static Process StartStalledWorker()
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("[Console]::WriteLine('ToolsBox.FilePathQuery.v1'); [Console]::Out.Flush(); [Console]::ReadLine() | Out-Null; [Threading.Thread]::Sleep(10000)");
        return Process.Start(start)!;
    }
}
