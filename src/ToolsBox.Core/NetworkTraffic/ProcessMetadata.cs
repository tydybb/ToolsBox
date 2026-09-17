namespace ToolsBox.Core.NetworkTraffic;

public sealed record ProcessMetadata(
    ProcessIdentity Identity,
    string ProcessName,
    string? ExecutablePath,
    bool IsAccessible,
    bool HasExited);
