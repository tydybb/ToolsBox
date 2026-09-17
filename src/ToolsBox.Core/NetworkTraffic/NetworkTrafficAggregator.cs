namespace ToolsBox.Core.NetworkTraffic;

public sealed class NetworkTrafficAggregator
{
    private readonly IProcessMetadataProvider _metadataProvider;
    private readonly object _syncRoot = new();
    private readonly List<NetworkTrafficDelta> _pending = [];
    private readonly Dictionary<ProcessIdentity, ProcessState> _states = [];
    private DateTimeOffset _lastSampleAt;
    private bool _isRunning;

    public NetworkTrafficAggregator(IProcessMetadataProvider metadataProvider)
    {
        _metadataProvider = metadataProvider;
    }

    public void BeginSession(DateTimeOffset startedAt)
    {
        lock (_syncRoot)
        {
            _pending.Clear();
            _states.Clear();
            _lastSampleAt = startedAt;
            _isRunning = true;
        }
    }

    public void Apply(IEnumerable<NetworkTrafficDelta> deltas)
    {
        lock (_syncRoot)
        {
            if (!_isRunning)
            {
                return;
            }

            foreach (NetworkTrafficDelta delta in deltas)
            {
                if (delta.ProcessId > 0 && delta.ByteCount > 0)
                {
                    _pending.Add(delta);
                }
            }
        }
    }

    public async Task<IReadOnlyList<ApplicationTrafficSnapshot>> CreateSnapshotAsync(
        DateTimeOffset sampledAt,
        CancellationToken cancellationToken = default)
    {
        List<NetworkTrafficDelta> pending;
        bool isRunning;
        DateTimeOffset previousSampleAt;
        lock (_syncRoot)
        {
            pending = [.. _pending];
            _pending.Clear();
            isRunning = _isRunning;
            previousSampleAt = _lastSampleAt;
            _lastSampleAt = sampledAt;
        }

        foreach (IGrouping<PendingIdentity, NetworkTrafficDelta> group in pending.GroupBy(
                     delta => new PendingIdentity(delta.ProcessId, delta.ProcessStartedAt)))
        {
            ProcessMetadata metadata = await _metadataProvider.GetAsync(
                group.Key.ProcessId,
                group.Key.StartedAt,
                cancellationToken);

            if (!_states.TryGetValue(metadata.Identity, out ProcessState? state))
            {
                state = new ProcessState(metadata);
                _states.Add(metadata.Identity, state);
            }
            else
            {
                state.Metadata = metadata;
            }

            foreach (NetworkTrafficDelta delta in group)
            {
                state.Add(delta.Direction, delta.ByteCount);
            }
        }

        double elapsedSeconds = (sampledAt - previousSampleAt).TotalSeconds;
        if (!isRunning || elapsedSeconds <= 0)
        {
            elapsedSeconds = 0;
        }

        ProcessTrafficSnapshot[] processSnapshots = _states.Values
            .Select(state => state.CreateSnapshot(elapsedSeconds))
            .ToArray();

        return processSnapshots
            .GroupBy(GetApplicationKey, StringComparer.OrdinalIgnoreCase)
            .Select(CreateApplicationSnapshot)
            .OrderByDescending(item => item.UploadBytesPerSecond + item.DownloadBytesPerSecond)
            .ThenBy(item => item.ApplicationName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public void MarkStopped(DateTimeOffset stoppedAt)
    {
        lock (_syncRoot)
        {
            _isRunning = false;
            _lastSampleAt = stoppedAt;
        }
    }

    private static string GetApplicationKey(ProcessTrafficSnapshot process)
    {
        if (!string.IsNullOrWhiteSpace(process.ExecutablePath))
        {
            return NormalizePath(process.ExecutablePath);
        }

        return $"RESTRICTED:{process.Identity.ProcessId}:{process.Identity.StartedAt.UtcTicks}";
    }

    private static ApplicationTrafficSnapshot CreateApplicationSnapshot(
        IGrouping<string, ProcessTrafficSnapshot> group)
    {
        ProcessTrafficSnapshot[] processes = group
            .OrderBy(item => item.Identity.ProcessId)
            .ThenBy(item => item.Identity.StartedAt)
            .ToArray();
        ProcessTrafficSnapshot representative = processes[0];
        string? path = processes.Select(item => item.ExecutablePath).FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));

        return new ApplicationTrafficSnapshot(
            group.Key,
            representative.ProcessName,
            path,
            path is not null && processes.Any(item => item.IsAccessible),
            processes.Sum(item => item.UploadBytesPerSecond),
            processes.Sum(item => item.DownloadBytesPerSecond),
            processes.Sum(item => item.TotalUploadBytes),
            processes.Sum(item => item.TotalDownloadBytes),
            processes);
    }

    internal static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();

    private sealed record PendingIdentity(int ProcessId, DateTimeOffset? StartedAt);

    private sealed class ProcessState(ProcessMetadata metadata)
    {
        private long _windowUpload;
        private long _windowDownload;

        public ProcessMetadata Metadata { get; set; } = metadata;
        public long TotalUpload { get; private set; }
        public long TotalDownload { get; private set; }

        public void Add(NetworkTrafficDirection direction, long bytes)
        {
            if (direction == NetworkTrafficDirection.Upload)
            {
                _windowUpload += bytes;
                TotalUpload += bytes;
            }
            else
            {
                _windowDownload += bytes;
                TotalDownload += bytes;
            }
        }

        public ProcessTrafficSnapshot CreateSnapshot(double elapsedSeconds)
        {
            long uploadRate = elapsedSeconds > 0 ? (long)Math.Round(_windowUpload / elapsedSeconds) : 0;
            long downloadRate = elapsedSeconds > 0 ? (long)Math.Round(_windowDownload / elapsedSeconds) : 0;
            _windowUpload = 0;
            _windowDownload = 0;

            return new ProcessTrafficSnapshot(
                Metadata.Identity,
                Metadata.ProcessName,
                Metadata.ExecutablePath,
                Metadata.IsAccessible,
                Metadata.HasExited,
                uploadRate,
                downloadRate,
                TotalUpload,
                TotalDownload);
        }
    }
}
