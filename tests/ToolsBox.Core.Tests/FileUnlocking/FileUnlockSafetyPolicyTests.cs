using ToolsBox.Core.FileUnlocking;

namespace ToolsBox.Core.Tests.FileUnlocking;

public sealed class FileUnlockSafetyPolicyTests
{
    [Theory]
    [InlineData(0, "Idle")]
    [InlineData(4, "System")]
    [InlineData(100, "csrss")]
    [InlineData(200, "LSASS.EXE")]
    public void CanOperate_RejectsProtectedProcesses(int pid, string processName)
    {
        FileLockEntry entry = CreateEntry(pid, processName);

        bool allowed = FileUnlockSafetyPolicy.CanOperate(entry, 9999, out string reason);

        Assert.False(allowed);
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void CanOperate_RejectsToolsBoxItself()
    {
        FileLockEntry entry = CreateEntry(3456, "ToolsBox.App");

        Assert.False(FileUnlockSafetyPolicy.CanOperate(entry, 3456, out _));
    }

    [Fact]
    public void CanOperate_AllowsOrdinaryProcess()
    {
        FileLockEntry entry = CreateEntry(1234, "notepad");

        Assert.True(FileUnlockSafetyPolicy.CanOperate(entry, 9999, out string reason));
        Assert.Equal(string.Empty, reason);
    }

    [Fact]
    public void CanOperate_RejectsEntryWithoutProcessStartTime()
    {
        FileLockEntry entry = CreateEntry(1234, "notepad") with { ProcessStartedAt = null };

        Assert.False(FileUnlockSafetyPolicy.CanOperate(entry, 9999, out string reason));
        Assert.Contains("进程身份", reason);
    }

    private static FileLockEntry CreateEntry(int pid, string processName) =>
        new(@"C:\Temp\locked.txt", 42, pid, processName, @"C:\Windows\notepad.exe", DateTimeOffset.UtcNow, false);
}
