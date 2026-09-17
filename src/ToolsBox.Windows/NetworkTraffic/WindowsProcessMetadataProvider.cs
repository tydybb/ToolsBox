using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.Windows.NetworkTraffic;

public sealed class WindowsProcessMetadataProvider : IProcessMetadataProvider
{
    private readonly ConcurrentDictionary<ProcessIdentity, ProcessMetadata> _cache = new();

    public ValueTask<ProcessMetadata> GetAsync(
        int processId,
        DateTimeOffset? knownStartTime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (knownStartTime.HasValue &&
            _cache.TryGetValue(new ProcessIdentity(processId, knownStartTime.Value), out ProcessMetadata? cached))
        {
            return ValueTask.FromResult(cached);
        }

        ProcessMetadata metadata = ReadMetadata(processId, knownStartTime);
        if (metadata.IsAccessible)
        {
            _cache[metadata.Identity] = metadata;
        }

        return ValueTask.FromResult(metadata);
    }

    private static ProcessMetadata ReadMetadata(int processId, DateTimeOffset? knownStartTime)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            string processName = SafeReadReference(() => process.ProcessName) ?? $"PID {processId}";
            DateTimeOffset startedAt = knownStartTime ??
                                       SafeReadValue(() => new DateTimeOffset(process.StartTime).ToUniversalTime()) ??
                                       DateTimeOffset.MinValue;
            string? path = SafeReadReference(() => process.MainModule?.FileName);
            bool hasExited = SafeReadValue(() => process.HasExited) ?? false;

            return new ProcessMetadata(
                new ProcessIdentity(processId, startedAt),
                processName,
                path,
                !string.IsNullOrWhiteSpace(path),
                hasExited);
        }
        catch (Exception exception) when (IsExpectedProcessFailure(exception))
        {
            return new ProcessMetadata(
                new ProcessIdentity(processId, knownStartTime ?? DateTimeOffset.MinValue),
                $"PID {processId}",
                null,
                false,
                true);
        }
    }

    private static T? SafeReadReference<T>(Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (IsExpectedProcessFailure(exception))
        {
            return default;
        }
    }

    private static T? SafeReadValue<T>(Func<T> read) where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (IsExpectedProcessFailure(exception))
        {
            return null;
        }
    }

    private static bool IsExpectedProcessFailure(Exception exception) =>
        exception is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException;
}
