namespace ToolsBox.Core.NetworkTraffic;

public interface INetworkTrafficSource : IAsyncDisposable
{
    long DroppedEventCount { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<NetworkTrafficDelta> ReadAllAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
