using ToolsBox.Windows.NetworkTraffic;

namespace ToolsBox.Windows.Tests.NetworkTraffic;

public sealed class EtwNetworkTrafficSourceTests
{
    [Fact]
    public void InitializeSession_EnablesKernelProviderBeforeSubscribing()
    {
        var calls = new List<string>();

        EtwNetworkTrafficSource.InitializeSession(
            () => calls.Add("enable"),
            () => calls.Add("subscribe"));

        Assert.Equal(["enable", "subscribe"], calls);
    }
}
