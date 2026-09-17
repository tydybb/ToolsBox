namespace ToolsBox.App.NetworkTraffic;

public interface INetworkTrafficClock
{
    DateTimeOffset UtcNow { get; }
    IAsyncEnumerable<DateTimeOffset> GetTicksAsync(CancellationToken cancellationToken = default);
}

public sealed class SystemNetworkTrafficClock : INetworkTrafficClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public async IAsyncEnumerable<DateTimeOffset> GetTicksAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            yield return UtcNow;
        }
    }
}
