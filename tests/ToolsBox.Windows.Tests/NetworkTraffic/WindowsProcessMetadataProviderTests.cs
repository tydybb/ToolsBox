using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using ToolsBox.Windows.NetworkTraffic;

namespace ToolsBox.Windows.Tests.NetworkTraffic;

public sealed class WindowsProcessMetadataProviderTests
{
    [Fact]
    public async Task GetAsync_ProtectedProcessDoesNotThrowFirstChanceWin32Exceptions()
    {
        var exceptions = new List<string>();
        int threadId = Environment.CurrentManagedThreadId;
        void OnException(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (Environment.CurrentManagedThreadId == threadId && args.Exception is Win32Exception)
                exceptions.Add(args.Exception.ToString());
        }

        AppDomain.CurrentDomain.FirstChanceException += OnException;
        try
        {
            var provider = new WindowsProcessMetadataProvider();
            for (int i = 0; i < 3; i++)
            {
                var metadata = await provider.GetAsync(4, null);
                Assert.Equal(4, metadata.Identity.ProcessId);
                Assert.False(metadata.HasExited);
            }
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnException;
        }

        Assert.True(exceptions.Count == 0, string.Join(Environment.NewLine, exceptions));
    }

    [Fact]
    public async Task GetAsync_ResolvesCurrentProcessIdentityAndPath()
    {
        using Process current = Process.GetCurrentProcess();
        var provider = new WindowsProcessMetadataProvider();

        var result = await provider.GetAsync(current.Id, null);

        Assert.Equal(current.Id, result.Identity.ProcessId);
        Assert.Equal(current.StartTime.ToUniversalTime(), result.Identity.StartedAt.UtcDateTime);
        Assert.False(string.IsNullOrWhiteSpace(result.ProcessName));
        Assert.False(string.IsNullOrWhiteSpace(result.ExecutablePath));
        Assert.True(Path.IsPathFullyQualified(result.ExecutablePath));
        Assert.True(result.IsAccessible);
        Assert.False(result.HasExited);
    }

    [Fact]
    public async Task GetAsync_ReturnsExitedMetadataWhenPidDoesNotExist()
    {
        var provider = new WindowsProcessMetadataProvider();
        DateTimeOffset knownStart = DateTimeOffset.UnixEpoch;

        var result = await provider.GetAsync(int.MaxValue, knownStart);

        Assert.Equal(new Core.NetworkTraffic.ProcessIdentity(int.MaxValue, knownStart), result.Identity);
        Assert.Equal($"PID {int.MaxValue}", result.ProcessName);
        Assert.Null(result.ExecutablePath);
        Assert.False(result.IsAccessible);
        Assert.True(result.HasExited);
    }

    [Fact]
    public async Task GetAsync_DoesNotAttributeOldIdentityToCurrentPid()
    {
        var provider = new WindowsProcessMetadataProvider();
        var result = await provider.GetAsync(Environment.ProcessId, DateTimeOffset.UnixEpoch);

        Assert.Equal(DateTimeOffset.UnixEpoch, result.Identity.StartedAt);
        Assert.True(result.HasExited);
        Assert.False(result.IsAccessible);
        Assert.Null(result.ExecutablePath);
    }
}
