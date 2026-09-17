using System.Net.Sockets;
using ToolsBox.Core.Ports;

namespace ToolsBox.Core.Tests.Ports;

public sealed class PortMonitorServiceTests
{
    [Fact]
    public async Task RefreshAsync_OnSuccess_ReplacesSnapshotAndClearsError()
    {
        PortEntry entry = CreateEntry(8080);
        var service = new PortMonitorService(new StubProvider([entry]));

        bool refreshed = await service.RefreshAsync();

        Assert.True(refreshed);
        Assert.Equal([entry], service.Snapshot);
        Assert.Null(service.LastError);
        Assert.NotNull(service.LastRefreshedAt);
    }

    [Fact]
    public async Task RefreshAsync_OnFailure_KeepsPreviousSnapshotAndExposesError()
    {
        PortEntry entry = CreateEntry(8080);
        var provider = new SequenceProvider([entry], new InvalidOperationException("boom"));
        var service = new PortMonitorService(provider);
        await service.RefreshAsync();

        bool refreshed = await service.RefreshAsync();

        Assert.False(refreshed);
        Assert.Equal([entry], service.Snapshot);
        Assert.Equal("boom", service.LastError);
    }

    [Fact]
    public async Task RefreshAsync_WhenAlreadyRefreshing_DoesNotStartAnotherRequest()
    {
        var provider = new BlockingProvider();
        var service = new PortMonitorService(provider);
        Task<bool> first = service.RefreshAsync();
        await provider.Started.Task;

        bool second = await service.RefreshAsync();
        provider.Complete();
        await first;

        Assert.False(second);
        Assert.Equal(1, provider.CallCount);
    }

    private static PortEntry CreateEntry(int port) =>
        new("TCP", AddressFamily.InterNetwork, "127.0.0.1", port, "0.0.0.0", 0, "Listen", 1, "test");

    private sealed class StubProvider(IReadOnlyList<PortEntry> entries) : IPortSnapshotProvider
    {
        public Task<IReadOnlyList<PortEntry>> GetSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(entries);
    }

    private sealed class SequenceProvider(IReadOnlyList<PortEntry> entries, Exception exception) : IPortSnapshotProvider
    {
        private int _calls;

        public Task<IReadOnlyList<PortEntry>> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                return Task.FromResult(entries);
            }

            return Task.FromException<IReadOnlyList<PortEntry>>(exception);
        }
    }

    private sealed class BlockingProvider : IPortSnapshotProvider
    {
        private readonly TaskCompletionSource<IReadOnlyList<PortEntry>> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<PortEntry>> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            Started.TrySetResult();
            return _completion.Task;
        }

        public void Complete() => _completion.TrySetResult([]);
    }
}
