namespace ToolsBox.Core.NetworkTraffic;

public sealed record ProcessTrafficSnapshot(
    ProcessIdentity Identity,
    string ProcessName,
    string? ExecutablePath,
    bool IsAccessible,
    bool HasExited,
    long UploadBytesPerSecond,
    long DownloadBytesPerSecond,
    long TotalUploadBytes,
    long TotalDownloadBytes);
