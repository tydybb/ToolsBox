using System.Net.Sockets;
using ToolsBox.Windows.Ports;

namespace ToolsBox.Windows.Tests.Ports;

public sealed class WindowsPortSnapshotProviderTests
{
    [Fact]
    public async Task GetSnapshotAsync_ReturnsWellFormedWindowsNetworkRows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var provider = new WindowsPortSnapshotProvider();

        var entries = await provider.GetSnapshotAsync();

        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
        {
            Assert.Contains(entry.Protocol, new[] { "TCP", "UDP" });
            Assert.Contains(entry.AddressFamily, new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 });
            Assert.InRange(entry.LocalPort, 0, 65535);
            Assert.True(entry.ProcessId >= 0);
            Assert.False(string.IsNullOrWhiteSpace(entry.LocalAddress));
            Assert.False(string.IsNullOrWhiteSpace(entry.ProcessName));
        });
    }
}
