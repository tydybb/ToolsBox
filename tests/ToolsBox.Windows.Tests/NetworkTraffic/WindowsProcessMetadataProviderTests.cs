using System.Diagnostics;
using ToolsBox.Windows.NetworkTraffic;

namespace ToolsBox.Windows.Tests.NetworkTraffic;

public sealed class WindowsProcessMetadataProviderTests
{
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
}
