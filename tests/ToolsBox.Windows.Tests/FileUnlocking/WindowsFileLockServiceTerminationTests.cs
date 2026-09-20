using System.ComponentModel;
using ToolsBox.Core.FileUnlocking;
using ToolsBox.Windows.FileUnlocking;

namespace ToolsBox.Windows.Tests.FileUnlocking;

public sealed class WindowsFileLockServiceTerminationTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TerminateProcess_KillsOnlyTheSelectedProcessAndWaitsForAtMostFiveSeconds()
    {
        FileLockEntry entry = CreateEntry();
        var process = new FakeProcess(entry);
        var openedProcessIds = new List<int>();

        FileUnlockResult result = WindowsFileLockService.TerminateProcess(entry, pid =>
        {
            openedProcessIds.Add(pid);
            return process;
        });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(new[] { entry.ProcessId }, openedProcessIds);
        Assert.Equal(new[] { false }, process.KillRequests);
        Assert.Equal(new[] { 5000 }, process.WaitTimeouts);
        Assert.True(process.Disposed);
    }

    [Theory]
    [InlineData("explorer")]
    [InlineData("EXPLORER.EXE")]
    [InlineData("dwm")]
    [InlineData("sihost")]
    [InlineData("ShellExperienceHost")]
    [InlineData("StartMenuExperienceHost")]
    public void TerminateProcess_RejectsDesktopProcessesBeforeOpeningThem(string processName)
    {
        AssertRejectedWithoutOpening(CreateEntry() with { ProcessName = processName });
    }

    [Fact]
    public void TerminateProcess_RejectsTheCurrentProcessBeforeOpeningIt()
    {
        AssertRejectedWithoutOpening(CreateEntry() with { ProcessId = Environment.ProcessId });
    }

    [Fact]
    public void TerminateProcess_RejectsMissingIdentityBeforeOpeningTheProcess()
    {
        AssertRejectedWithoutOpening(CreateEntry() with { ProcessStartedAt = null });
    }

    [Fact]
    public void TerminateProcess_RejectsReusedPidAndDisposesTheProcess()
    {
        FileLockEntry entry = CreateEntry();
        var process = new FakeProcess(entry) { StartTime = StartedAt.AddSeconds(1).UtcDateTime };

        FileUnlockResult result = WindowsFileLockService.TerminateProcess(entry, _ => process);

        Assert.False(result.Succeeded);
        Assert.Contains("PID", result.Message);
        AssertNotTerminatedAndDisposed(process);
    }

    [Fact]
    public void TerminateProcess_RejectsUnexpectedProcessId()
    {
        FileLockEntry entry = CreateEntry();
        var process = new FakeProcess(entry) { Id = entry.ProcessId + 1 };

        FileUnlockResult result = WindowsFileLockService.TerminateProcess(entry, _ => process);

        Assert.False(result.Succeeded);
        AssertNotTerminatedAndDisposed(process);
    }

    [Fact]
    public void TerminateProcess_RejectsAlreadyExitedProcess()
    {
        FileLockEntry entry = CreateEntry();
        var process = new FakeProcess(entry) { HasExited = true };

        FileUnlockResult result = WindowsFileLockService.TerminateProcess(entry, _ => process);

        Assert.False(result.Succeeded);
        Assert.Contains("退出", result.Message);
        AssertNotTerminatedAndDisposed(process);
    }

    [Fact]
    public void TerminateProcess_RejectsProtectedLiveNameEvenIfScanNameIsDifferent()
    {
        FileLockEntry entry = CreateEntry();
        var process = new FakeProcess(entry) { ProcessName = "explorer" };

        FileUnlockResult result = WindowsFileLockService.TerminateProcess(entry, _ => process);

        Assert.False(result.Succeeded);
        Assert.Contains("explorer", result.Message);
        AssertNotTerminatedAndDisposed(process);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unavailable")]
    [InlineData("denied")]
    public void TerminateProcess_ReturnsFailureWhenTheProcessCannotBeOpened(string failure)
    {
        Exception exception = failure switch
        {
            "missing" => new ArgumentException("Process no longer exists."),
            "unavailable" => new InvalidOperationException("Process is unavailable."),
            _ => new Win32Exception(5, "Access is denied.")
        };

        FileUnlockResult result = WindowsFileLockService.TerminateProcess(CreateEntry(), _ => throw exception);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public void TerminateProcess_RejectsUnreadableStartTimeAndDisposesTheProcess()
    {
        FileLockEntry entry = CreateEntry();
        var process = new FakeProcess(entry) { StartTimeError = new Win32Exception(5, "Access is denied.") };

        FileUnlockResult result = WindowsFileLockService.TerminateProcess(entry, _ => process);

        Assert.False(result.Succeeded);
        AssertNotTerminatedAndDisposed(process);
    }

    [Fact]
    public void TerminateProcess_ReturnsTimeoutFailureWithoutRetryingTheKill()
    {
        FileLockEntry entry = CreateEntry();
        var process = new FakeProcess(entry) { ExitsWhenWaited = false };

        FileUnlockResult result = WindowsFileLockService.TerminateProcess(entry, _ => process);

        Assert.False(result.Succeeded);
        Assert.Contains("限定时间", result.Message);
        Assert.Equal(new[] { false }, process.KillRequests);
        Assert.Equal(new[] { 5000 }, process.WaitTimeouts);
        Assert.True(process.Disposed);
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("exited")]
    [InlineData("unsupported")]
    public void TerminateProcess_ReturnsKillFailureWithoutWaitingOrRetrying(string failure)
    {
        FileLockEntry entry = CreateEntry();
        Exception exception = failure switch
        {
            "denied" => new Win32Exception(5, "Access is denied."),
            "exited" => new InvalidOperationException("Process has exited."),
            _ => new NotSupportedException("Operation is unsupported.")
        };
        var process = new FakeProcess(entry) { KillError = exception };

        FileUnlockResult result = WindowsFileLockService.TerminateProcess(entry, _ => process);

        Assert.False(result.Succeeded);
        Assert.Equal(exception.Message, result.Message);
        Assert.Equal(new[] { false }, process.KillRequests);
        Assert.Empty(process.WaitTimeouts);
        Assert.True(process.Disposed);
    }

    private static FileLockEntry CreateEntry() =>
        new(@"C:\Temp\locked.txt", 42, int.MaxValue - 1, "sample-app", @"C:\Tools\sample-app.exe", StartedAt, false);

    private static void AssertRejectedWithoutOpening(FileLockEntry entry)
    {
        bool opened = false;
        var process = new FakeProcess(entry);
        FileUnlockResult result = WindowsFileLockService.TerminateProcess(entry, _ =>
        {
            opened = true;
            return process;
        });

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Message);
        Assert.False(opened);
        Assert.Empty(process.KillRequests);
    }

    private static void AssertNotTerminatedAndDisposed(FakeProcess process)
    {
        Assert.Empty(process.KillRequests);
        Assert.Empty(process.WaitTimeouts);
        Assert.True(process.Disposed);
    }

    // All OS interactions are replaced here; no test starts, opens, or kills a real process.
    private sealed class FakeProcess(FileLockEntry entry) : IFileUnlockProcess
    {
        private DateTime _startTime = entry.ProcessStartedAt?.UtcDateTime ?? StartedAt.UtcDateTime;
        public int Id { get; init; } = entry.ProcessId;
        public string ProcessName { get; init; } = entry.ProcessName;
        public DateTime StartTime
        {
            get => StartTimeError is null ? _startTime : throw StartTimeError;
            init => _startTime = value;
        }
        public bool HasExited { get; set; }
        public bool ExitsWhenWaited { get; init; } = true;
        public Exception? StartTimeError { get; init; }
        public Exception? KillError { get; init; }
        public List<bool> KillRequests { get; } = [];
        public List<int> WaitTimeouts { get; } = [];
        public bool Disposed { get; private set; }

        public void Kill(bool entireProcessTree)
        {
            KillRequests.Add(entireProcessTree);
            if (KillError is not null) throw KillError;
        }

        public bool WaitForExit(int milliseconds)
        {
            WaitTimeouts.Add(milliseconds);
            HasExited = ExitsWhenWaited;
            return HasExited;
        }

        public void Dispose() => Disposed = true;
    }
}
