using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ToolsBox.Core.NetworkTraffic;
using ToolsBox.Windows.NetworkTraffic;

namespace ToolsBox.Windows.Tests.NetworkTraffic;

public sealed class NetworkTrafficPumpTests
{
    [Fact]
    public async Task Stop_WaitsForPendingReadBeforeDisposingEnumerator()
    {
        var source = new PendingSource();
        var messages = Channel.CreateUnbounded<NetworkHelperMessage>();
        using var cancellation = new CancellationTokenSource();
        Task pump = ElevatedNetworkHelper.RunTrafficPumpAsync(source, messages.Writer, cancellation.Token);
        await source.Reading.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        await pump.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(source.ReadFinished);
    }

    [Fact]
    public async Task ContinuousTraffic_FlushesBeforeBatchIsFull()
    {
        var messages = Channel.CreateUnbounded<NetworkHelperMessage>();
        using var cancellation = new CancellationTokenSource();
        Task pump = ElevatedNetworkHelper.RunTrafficPumpAsync(new ContinuousSource(), messages.Writer, cancellation.Token);
        try
        {
            NetworkHelperMessage batch = await messages.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("traffic-batch", batch.Type);
            Assert.InRange(batch.Payload.GetProperty("Deltas").GetArrayLength(), 1, 2047);
        }
        finally
        {
            cancellation.Cancel();
            await pump.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private abstract class TestSource : INetworkTrafficSource
    {
        public long DroppedEventCount => 0;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public abstract IAsyncEnumerable<NetworkTrafficDelta> ReadAllAsync(CancellationToken cancellationToken = default);
    }

    private sealed class PendingSource : TestSource
    {
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ReadFinished { get; private set; }

        public override async IAsyncEnumerable<NetworkTrafficDelta> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Reading.SetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            // Model an async reader that needs time to unwind after cancellation.
            await Task.Delay(100);
            ReadFinished = true;
            yield break;
        }
    }

    private sealed class ContinuousSource : TestSource
    {
        public override async IAsyncEnumerable<NetworkTrafficDelta> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (true)
            {
                await Task.Delay(10, cancellationToken);
                yield return new NetworkTrafficDelta(42, null, NetworkTrafficDirection.Upload, 1024, DateTimeOffset.UtcNow);
            }
        }
    }
}
