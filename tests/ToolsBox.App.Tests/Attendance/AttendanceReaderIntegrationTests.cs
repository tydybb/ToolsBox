using System.IO;
using ToolsBox.App.Attendance;
using ToolsBox.Core.Attendance;

namespace ToolsBox.App.Tests.Attendance;

public sealed class AttendanceReaderIntegrationTests
{
    [Fact]
    public void ReaderWorkerIsRoutedBeforeAdministratorAndMainInstanceStartup()
    {
        string source = ReadSource("src/ToolsBox.App/App.xaml.cs");
        int route = source.IndexOf("--attendance-read-worker", StringComparison.Ordinal);
        Assert.True(route >= 0, "Missing headless attendance reader route.");
        Assert.True(route < source.IndexOf("AdministratorStartup.EnsureAdministrator", StringComparison.Ordinal));
        Assert.True(route < source.IndexOf("MainInstanceGate.Acquire", StringComparison.Ordinal));
        Assert.Contains("AttendanceReadWorker.RunAsync", source);
    }

    [Fact]
    public void BothHostReadEntrypointsUseOneIsolatedClientAndDisposeIt()
    {
        string source = ReadSource("src/ToolsBox.App/Attendance/AttendanceHost.cs");
        Assert.DoesNotContain("DingTalkAttendanceChecker", source);
        Assert.Contains("AttendanceReadClient", source);
        Assert.Contains("_reader.CheckAsync(directory,now)", source);
        Assert.Contains("_reader.CheckAsync(options.AccountDirectory,now)", source);
        Assert.Contains("_reader.Dispose()", source);
    }

    [Fact]
    public async Task SameCoordinatorRecoversAfterReadFailureAndReadsAgainNextDay()
    {
        string root = Path.Combine(Path.GetTempPath(), "ToolsBox-reader-integration-" + Guid.NewGuid().ToString("N"));
        var store = new AttendanceStore(root);
        try
        {
            DateTime now = new(2026, 10, 13, 8, 0, 0);
            int reads = 0;
            store.Update(o => o with { Enabled = true, AccountDirectory = root });
            var coordinator = new AttendanceCoordinator(store, (_, _) =>
            {
                reads++;
                if (reads == 1) throw new TypeInitializationException("Microsoft.Data.Sqlite.SqliteConnection", new DllNotFoundException());
                return Task.FromResult(new AttendanceStatus(true, true, "07:50", null, 1, null));
            }, () => now);

            await coordinator.CheckAsync(true, true);
            Assert.Null(store.LoadSnapshot()!.ClockInAt);
            now = now.AddMinutes(5);
            await coordinator.CheckAsync(true, true);
            Assert.Equal(now.Date.AddHours(7).AddMinutes(50), store.LoadSnapshot()!.ClockInAt);
            now = now.AddDays(1);
            await coordinator.CheckAsync(true, true);
            Assert.Equal(3, reads);
            Assert.Equal(now.Date.AddHours(7).AddMinutes(50), store.LoadSnapshot()!.ClockInAt);
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    [Fact]
    public async Task PreviewCannotPublishAfterItsOwnerStops()
    {
        var constructor = typeof(AttendancePreviewWorker).GetConstructors()
            .SingleOrDefault(c => c.GetParameters().Length == 4);
        Assert.NotNull(constructor);
        string root = Path.Combine(Path.GetTempPath(), "ToolsBox-reader-preview-" + Guid.NewGuid().ToString("N"));
        var store = new AttendanceStore(root);
        try
        {
            DateTime now = new(2026, 10, 13, 8, 0, 0);
            store.Update(o => o with { Enabled = true });
            store.SavePreviewRequest(new(Guid.NewGuid(), root, now));
            bool active = true;
            var pending = new TaskCompletionSource<AttendanceStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<string, DateTime, Task<AttendanceStatus>> read = (_, _) => pending.Task;
            Func<DateTime> clock = () => now;
            Func<bool> canPublish = () => active;
            var preview = (AttendancePreviewWorker)constructor!.Invoke([store, read, clock, canPublish]);
            Task task = preview.TickAsync();
            active = false;
            pending.SetResult(AttendanceStatus.Failure("cancelled"));
            await task;
            Assert.Null(store.LoadPreviewResult());
        }
        finally
        {
            DeleteTestDirectory(root);
        }
    }

    private static string ReadSource(string relative)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(relative);
    }

    private static void DeleteTestDirectory(string root)
    {
        string full = Path.GetFullPath(root);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), Path.GetDirectoryName(full));
        Assert.StartsWith("ToolsBox-reader-", Path.GetFileName(full));
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }
}
