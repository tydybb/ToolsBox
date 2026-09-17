using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.Core.Tests.NetworkTraffic;

public sealed class NetworkTrafficAggregatorTests
{
    private static readonly DateTimeOffset SessionStart = new(2026, 9, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateSnapshotAsync_AggregatesProcessesByNormalizedExecutablePath()
    {
        DateTimeOffset firstStart = SessionStart.AddMinutes(-2);
        DateTimeOffset secondStart = SessionStart.AddMinutes(-1);
        var metadata = new FakeProcessMetadataProvider(
            new ProcessMetadata(new ProcessIdentity(120, firstStart), "Demo", @"C:\Apps\Demo.exe", true, false),
            new ProcessMetadata(new ProcessIdentity(121, secondStart), "Demo Helper", @"c:\apps\DEMO.exe", true, false));
        var aggregator = new NetworkTrafficAggregator(metadata);
        aggregator.BeginSession(SessionStart);

        aggregator.Apply([
            new NetworkTrafficDelta(120, firstStart, NetworkTrafficDirection.Upload, 2048, SessionStart.AddMilliseconds(100)),
            new NetworkTrafficDelta(121, secondStart, NetworkTrafficDirection.Download, 4096, SessionStart.AddMilliseconds(200))
        ]);

        ApplicationTrafficSnapshot app = Assert.Single(await aggregator.CreateSnapshotAsync(SessionStart.AddSeconds(1)));
        Assert.Equal(@"C:\APPS\DEMO.EXE", app.Key);
        Assert.Equal(2048, app.UploadBytesPerSecond);
        Assert.Equal(4096, app.DownloadBytesPerSecond);
        Assert.Equal(2048, app.TotalUploadBytes);
        Assert.Equal(4096, app.TotalDownloadBytes);
        Assert.Equal(2, app.Processes.Count);
    }

    [Fact]
    public async Task CreateSnapshotAsync_UsesElapsedTimeAndReturnsZeroRateWithoutNewTraffic()
    {
        DateTimeOffset processStart = SessionStart.AddMinutes(-1);
        var metadata = new FakeProcessMetadataProvider(
            new ProcessMetadata(new ProcessIdentity(42, processStart), "Uploader", @"C:\Uploader.exe", true, false));
        var aggregator = new NetworkTrafficAggregator(metadata);
        aggregator.BeginSession(SessionStart);
        aggregator.Apply([
            new NetworkTrafficDelta(42, processStart, NetworkTrafficDirection.Upload, 3000, SessionStart.AddMilliseconds(100))
        ]);

        ApplicationTrafficSnapshot first = Assert.Single(await aggregator.CreateSnapshotAsync(SessionStart.AddSeconds(2)));
        ApplicationTrafficSnapshot second = Assert.Single(await aggregator.CreateSnapshotAsync(SessionStart.AddSeconds(3)));

        Assert.Equal(1500, first.UploadBytesPerSecond);
        Assert.Equal(0, second.UploadBytesPerSecond);
        Assert.Equal(3000, second.TotalUploadBytes);
    }

    [Fact]
    public async Task CreateSnapshotAsync_KeepsReusedPidAsSeparateProcessIdentities()
    {
        DateTimeOffset oldStart = SessionStart.AddMinutes(-5);
        DateTimeOffset newStart = SessionStart.AddMilliseconds(500);
        var metadata = new FakeProcessMetadataProvider(
            new ProcessMetadata(new ProcessIdentity(77, oldStart), "Old", @"C:\Same.exe", true, true),
            new ProcessMetadata(new ProcessIdentity(77, newStart), "New", @"C:\Same.exe", true, false));
        var aggregator = new NetworkTrafficAggregator(metadata);
        aggregator.BeginSession(SessionStart);
        aggregator.Apply([
            new NetworkTrafficDelta(77, oldStart, NetworkTrafficDirection.Upload, 100, SessionStart.AddMilliseconds(100)),
            new NetworkTrafficDelta(77, newStart, NetworkTrafficDirection.Download, 200, SessionStart.AddMilliseconds(700))
        ]);

        ApplicationTrafficSnapshot app = Assert.Single(await aggregator.CreateSnapshotAsync(SessionStart.AddSeconds(1)));

        Assert.Equal(2, app.Processes.Count);
        Assert.Contains(app.Processes, item => item.Identity.StartedAt == oldStart && item.TotalUploadBytes == 100);
        Assert.Contains(app.Processes, item => item.Identity.StartedAt == newStart && item.TotalDownloadBytes == 200);
    }

    [Fact]
    public async Task MarkStopped_PreservesTotalsAndClearsRates()
    {
        DateTimeOffset processStart = SessionStart.AddMinutes(-1);
        var metadata = new FakeProcessMetadataProvider(
            new ProcessMetadata(new ProcessIdentity(9, processStart), "Demo", @"C:\Demo.exe", true, false));
        var aggregator = new NetworkTrafficAggregator(metadata);
        aggregator.BeginSession(SessionStart);
        aggregator.Apply([
            new NetworkTrafficDelta(9, processStart, NetworkTrafficDirection.Download, 512, SessionStart.AddMilliseconds(100))
        ]);
        _ = await aggregator.CreateSnapshotAsync(SessionStart.AddSeconds(1));

        aggregator.MarkStopped(SessionStart.AddSeconds(2));
        ApplicationTrafficSnapshot stopped = Assert.Single(await aggregator.CreateSnapshotAsync(SessionStart.AddSeconds(2)));

        Assert.Equal(0, stopped.DownloadBytesPerSecond);
        Assert.Equal(512, stopped.TotalDownloadBytes);
    }

    [Fact]
    public async Task CreateSnapshotAsync_KeepsRestrictedProcessesSeparateAndNonLimitable()
    {
        DateTimeOffset firstStart = SessionStart.AddMinutes(-2);
        DateTimeOffset secondStart = SessionStart.AddMinutes(-1);
        var metadata = new FakeProcessMetadataProvider(
            new ProcessMetadata(new ProcessIdentity(4, firstStart), "System", null, false, false),
            new ProcessMetadata(new ProcessIdentity(8, secondStart), "System", null, false, false));
        var aggregator = new NetworkTrafficAggregator(metadata);
        aggregator.BeginSession(SessionStart);
        aggregator.Apply([
            new NetworkTrafficDelta(4, firstStart, NetworkTrafficDirection.Download, 10, SessionStart),
            new NetworkTrafficDelta(8, secondStart, NetworkTrafficDirection.Download, 20, SessionStart)
        ]);

        IReadOnlyList<ApplicationTrafficSnapshot> apps = await aggregator.CreateSnapshotAsync(SessionStart.AddSeconds(1));

        Assert.Equal(2, apps.Count);
        Assert.All(apps, app => Assert.False(app.CanLimit));
    }

    private sealed class FakeProcessMetadataProvider(params ProcessMetadata[] values) : IProcessMetadataProvider
    {
        private readonly Dictionary<ProcessIdentity, ProcessMetadata> _values = values.ToDictionary(item => item.Identity);

        public ValueTask<ProcessMetadata> GetAsync(
            int processId,
            DateTimeOffset? knownStartTime,
            CancellationToken cancellationToken = default)
        {
            ProcessMetadata match = _values.Values.Single(item =>
                item.Identity.ProcessId == processId &&
                (!knownStartTime.HasValue || item.Identity.StartedAt == knownStartTime.Value));
            return ValueTask.FromResult(match);
        }
    }
}
