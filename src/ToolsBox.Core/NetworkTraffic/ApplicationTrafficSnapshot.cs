namespace ToolsBox.Core.NetworkTraffic;

public sealed record ApplicationTrafficSnapshot(
    string Key,
    string ApplicationName,
    string? ExecutablePath,
    bool CanLimit,
    long UploadBytesPerSecond,
    long DownloadBytesPerSecond,
    long TotalUploadBytes,
    long TotalDownloadBytes,
    IReadOnlyList<ProcessTrafficSnapshot> Processes);
