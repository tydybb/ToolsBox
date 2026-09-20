using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32.SafeHandles;
using ToolsBox.Windows.FileUnlocking;

namespace ToolsBox.Windows.Tests.FileUnlocking;

public sealed class FileUnlockProcessTests
{
    [Fact]
    public void Constructor_PinsTheProcessHandleBeforeAnyIdentityReadAndDisposeReleasesIt()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Observe only this test process. Never call Kill or launch any process.
        using Process process = Process.GetProcessById(Environment.ProcessId);
        FieldInfo cachedHandle = typeof(Process).GetField("_processHandle", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.NotNull(cachedHandle);
        Assert.Null(cachedHandle.GetValue(process));

        using var adapter = new FileUnlockProcess(process);

        // Read the field, not SafeHandle: accessing SafeHandle here would itself open it
        // and hide a constructor that forgot to pin the process before validation.
        SafeProcessHandle handle = Assert.IsType<SafeProcessHandle>(cachedHandle.GetValue(process));
        Assert.False(handle.IsInvalid);
        Assert.False(handle.IsClosed);
        Assert.Equal(Environment.ProcessId, adapter.Id);
        Assert.False(adapter.HasExited);
        Assert.Equal(process.StartTime, adapter.StartTime);
        Assert.False(adapter.WaitForExit(0));
        Assert.Same(handle, cachedHandle.GetValue(process));

        adapter.Dispose();

        Assert.True(handle.IsClosed);
    }

    [Fact]
    public void Constructor_DisposesTheProcessWhenPinningItsHandleFails()
    {
        // This Process has no associated OS process, so SafeHandle must fail locally.
        using var process = new Process();
        FieldInfo disposed = typeof(Process).GetField("_disposed", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.NotNull(disposed);
        Assert.False((bool)disposed.GetValue(process)!);

        Assert.Throws<InvalidOperationException>(() => new FileUnlockProcess(process));

        Assert.True((bool)disposed.GetValue(process)!);
    }
}
