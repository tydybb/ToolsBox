using System.IO;
using System.Threading.Channels;
using ToolsBox.App.NetworkTraffic;
using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.App.Tests.NetworkTraffic;

public sealed class NetworkTrafficViewModelTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartAndTick_PublishesSortedApplicationRows()
    {
        var source = new FakeTrafficSource();
        var clock = new TestNetworkTrafficClock(StartedAt);
        var metadata = new FakeMetadataProvider(
            new ProcessMetadata(new ProcessIdentity(10, StartedAt.AddMinutes(-1)), "Slow", @"C:\Slow.exe", true, false),
            new ProcessMetadata(new ProcessIdentity(20, StartedAt.AddMinutes(-1)), "Fast", @"C:\Fast.exe", true, false));
        using var viewModel = CreateViewModel(source, metadata, clock);

        await viewModel.StartAsync();
        source.Publish(
            new NetworkTrafficDelta(10, StartedAt.AddMinutes(-1), NetworkTrafficDirection.Upload, 100, StartedAt),
            new NetworkTrafficDelta(20, StartedAt.AddMinutes(-1), NetworkTrafficDirection.Download, 500, StartedAt));
        clock.Tick(StartedAt.AddSeconds(1));
        await WaitUntilAsync(() => viewModel.Items.Count == 2);

        Assert.True(viewModel.IsMonitoring);
        Assert.Equal("Fast", viewModel.Items[0].ApplicationName);
        Assert.Equal(500, viewModel.Items[0].DownloadBytesPerSecond);
        Assert.Equal("Slow", viewModel.Items[1].ApplicationName);
    }

    [Fact]
    public async Task StopAsync_PreservesTotalsAndClearsRates()
    {
        var source = new FakeTrafficSource();
        var clock = new TestNetworkTrafficClock(StartedAt);
        var metadata = CreateSingleMetadata();
        using var viewModel = CreateViewModel(source, metadata, clock);
        await viewModel.StartAsync();
        source.Publish(new NetworkTrafficDelta(42, StartedAt.AddMinutes(-1), NetworkTrafficDirection.Upload, 1024, StartedAt));
        clock.Tick(StartedAt.AddSeconds(1));
        await WaitUntilAsync(() => viewModel.Items.Count == 1);

        clock.Set(StartedAt.AddSeconds(2));
        await viewModel.StopAsync();

        Assert.False(viewModel.IsMonitoring);
        Assert.Equal(0, viewModel.Items[0].UploadBytesPerSecond);
        Assert.Equal(1024, viewModel.Items[0].TotalUploadBytes);
        Assert.Equal(1, source.StopCount);
    }

    [Fact]
    public async Task StartAsync_AfterStopBeginsEmptySession()
    {
        var source = new FakeTrafficSource();
        var clock = new TestNetworkTrafficClock(StartedAt);
        using var viewModel = CreateViewModel(source, CreateSingleMetadata(), clock);
        await viewModel.StartAsync();
        source.Publish(new NetworkTrafficDelta(42, StartedAt.AddMinutes(-1), NetworkTrafficDirection.Upload, 512, StartedAt));
        clock.Tick(StartedAt.AddSeconds(1));
        await WaitUntilAsync(() => viewModel.Items.Count == 1);
        clock.Set(StartedAt.AddSeconds(2));
        await viewModel.StopAsync();

        await viewModel.StartAsync();

        Assert.Empty(viewModel.Items);
        Assert.Equal(2, source.StartCount);
    }

    [Fact]
    public async Task StartAsync_WhenHelperFails_ShowsErrorAndKeepsStopped()
    {
        var source = new FakeTrafficSource { StartError = new UnauthorizedAccessException("需要管理员权限") };
        var clock = new TestNetworkTrafficClock(StartedAt);
        using var viewModel = CreateViewModel(source, CreateSingleMetadata(), clock);

        await viewModel.StartAsync();

        Assert.False(viewModel.IsMonitoring);
        Assert.Contains("需要管理员权限", viewModel.StatusText);
        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public async Task ReaderFailure_StopsSamplingAndPreservesLastSnapshot()
    {
        var source = new FakeTrafficSource();
        var clock = new TestNetworkTrafficClock(StartedAt);
        using var viewModel = CreateViewModel(source, CreateSingleMetadata(), clock);
        await viewModel.StartAsync();
        source.Publish(new NetworkTrafficDelta(42, StartedAt.AddMinutes(-1), NetworkTrafficDirection.Upload, 512, StartedAt));
        clock.Tick(StartedAt.AddSeconds(1));
        await WaitUntilAsync(() => viewModel.Items.Count == 1);

        source.Fail(new IOException("pipe closed"));
        await WaitUntilAsync(() => viewModel.StatusText.Contains("pipe closed", StringComparison.Ordinal));
        clock.Tick(StartedAt.AddSeconds(2));
        await Task.Delay(50);

        Assert.False(viewModel.IsMonitoring);
        Assert.Contains("pipe closed", viewModel.StatusText);
        Assert.Equal(512, viewModel.Items[0].TotalUploadBytes);
        Assert.Equal(1, source.StopCount);
    }

    [Fact]
    public void ApplicationRow_WithOwnedRuleAndForeignConflict_DisablesLimitAction()
    {
        ProcessMetadata metadata = CreateSingleMetadata().Single;
        var process = new ProcessTrafficSnapshot(
            metadata.Identity,
            metadata.ProcessName,
            metadata.ExecutablePath,
            true,
            false,
            0,
            0,
            0,
            0);
        var snapshot = new ApplicationTrafficSnapshot(
            metadata.ExecutablePath!,
            metadata.ProcessName,
            metadata.ExecutablePath,
            true,
            0,
            0,
            0,
            0,
            [process]);
        var rule = new BandwidthLimitRule(
            "BaoGeToolsBox-test",
            metadata.ExecutablePath!,
            BandwidthDirection.Upload,
            1024,
            true,
            true,
            "与外部策略冲突");

        var item = new ApplicationTrafficItemViewModel(snapshot, rule);

        Assert.False(item.CanLimit);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var viewModel = CreateViewModel(
            new FakeTrafficSource(),
            CreateSingleMetadata(),
            new TestNetworkTrafficClock(StartedAt));

        viewModel.Dispose();
        viewModel.Dispose();
    }

    private static NetworkTrafficViewModel CreateViewModel(
        FakeTrafficSource source,
        IProcessMetadataProvider metadata,
        TestNetworkTrafficClock clock) =>
        new(source, new FakeLimitService(), metadata, clock, new ImmediateDispatcher());

    private static FakeMetadataProvider CreateSingleMetadata() =>
        new(new ProcessMetadata(
            new ProcessIdentity(42, StartedAt.AddMinutes(-1)),
            "Uploader",
            @"C:\Uploader.exe",
            true,
            false));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FakeTrafficSource : INetworkTrafficSource
    {
        private readonly Channel<NetworkTrafficDelta> _channel = Channel.CreateUnbounded<NetworkTrafficDelta>();
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public Exception? StartError { get; init; }
        public long DroppedEventCount => 0;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            if (StartError is not null) throw StartError;
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<NetworkTrafficDelta> ReadAllAsync(CancellationToken cancellationToken = default) =>
            _channel.Reader.ReadAllAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public void Publish(params NetworkTrafficDelta[] deltas)
        {
            foreach (NetworkTrafficDelta delta in deltas) _channel.Writer.TryWrite(delta);
        }

        public void Fail(Exception exception) => _channel.Writer.TryComplete(exception);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeMetadataProvider(params ProcessMetadata[] values) : IProcessMetadataProvider
    {
        private readonly ProcessMetadata[] _values = values;

        public ProcessMetadata Single => Assert.Single(_values);

        public ValueTask<ProcessMetadata> GetAsync(int processId, DateTimeOffset? knownStartTime, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_values.Single(item => item.Identity.ProcessId == processId));
    }

    private sealed class FakeLimitService : IBandwidthLimitService
    {
        public BandwidthDirection SupportedDirections => BandwidthDirection.Upload;
        public Task<IReadOnlyList<BandwidthLimitRule>> GetRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BandwidthLimitRule>>([]);
        public Task<BandwidthLimitRule> SetUploadLimitAsync(string executablePath, ulong bitsPerSecond, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task RemoveUploadLimitAsync(string executablePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestNetworkTrafficClock(DateTimeOffset now) : INetworkTrafficClock
    {
        private readonly Channel<DateTimeOffset> _ticks = Channel.CreateUnbounded<DateTimeOffset>();
        public DateTimeOffset UtcNow { get; private set; } = now;

        public IAsyncEnumerable<DateTimeOffset> GetTicksAsync(CancellationToken cancellationToken = default) =>
            _ticks.Reader.ReadAllAsync(cancellationToken);

        public void Set(DateTimeOffset value) => UtcNow = value;

        public void Tick(DateTimeOffset value)
        {
            UtcNow = value;
            _ticks.Writer.TryWrite(value);
        }
    }

    private sealed class ImmediateDispatcher : INetworkTrafficDispatcher
    {
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }
}
