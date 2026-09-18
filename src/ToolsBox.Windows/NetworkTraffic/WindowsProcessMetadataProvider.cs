using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.Windows.NetworkTraffic;

public sealed class WindowsProcessMetadataProvider : IProcessMetadataProvider
{
    public ValueTask<ProcessMetadata> GetAsync(
        int processId,
        DateTimeOffset? knownStartTime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(ReadMetadata(processId, knownStartTime));
    }

    private static ProcessMetadata ReadMetadata(int processId, DateTimeOffset? knownStartTime)
    {
        if (processId < 0) return Unavailable(processId, knownStartTime, hasExited: true);

        // Module enumeration requests more access than we need and throws for protected
        // processes. Expected access/exit races are normal return codes on this path.
        using SafeProcessHandle process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, processId);
        if (process.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            return Unavailable(processId, knownStartTime, hasExited: processId != 0 && error == 87);
        }

        DateTimeOffset startedAt = knownStartTime ?? DateTimeOffset.MinValue;
        if (GetProcessTimes(process, out long creationTime, out _, out _, out _))
        {
            startedAt = new DateTimeOffset(DateTime.FromFileTimeUtc(creationTime));
            // Do not assign a reused PID's executable to an earlier process's traffic.
            if (knownStartTime.HasValue && knownStartTime.Value != startedAt)
                return Unavailable(processId, knownStartTime, hasExited: true);
        }

        string? path = ReadImagePath(process);
        bool hasExited = GetExitCodeProcess(process, out uint exitCode) && exitCode != 259 /* STILL_ACTIVE */;
        return new ProcessMetadata(
            new ProcessIdentity(processId, startedAt),
            path is null ? FallbackName(processId) : Path.GetFileNameWithoutExtension(path),
            path,
            path is not null,
            hasExited);
    }

    private static string? ReadImagePath(SafeProcessHandle process)
    {
        var buffer = new StringBuilder(1024);
        int length = buffer.Capacity;
        if (QueryFullProcessImageName(process, 0, buffer, ref length)) return buffer.ToString();
        if (Marshal.GetLastWin32Error() != 122 /* ERROR_INSUFFICIENT_BUFFER */) return null;

        buffer.EnsureCapacity(32768);
        length = buffer.Capacity;
        return QueryFullProcessImageName(process, 0, buffer, ref length) ? buffer.ToString() : null;
    }

    private static ProcessMetadata Unavailable(int processId, DateTimeOffset? startedAt, bool hasExited) =>
        new(new ProcessIdentity(processId, startedAt ?? DateTimeOffset.MinValue),
            FallbackName(processId), null, false, hasExited);

    private static string FallbackName(int processId) => processId switch
    {
        0 => "System Idle Process",
        4 => "System",
        _ => $"PID {processId}"
    };

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
}
