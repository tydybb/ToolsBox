namespace ToolsBox.Core.FileUnlocking;

public sealed record FileLockEntry(
    string LockedPath,
    long HandleValue,
    int ProcessId,
    string ProcessName,
    string ProcessPath,
    DateTimeOffset? ProcessStartedAt,
    bool IsAccessLimited)
{
    public bool IsSelected { get; set; }
    public string HandleDisplay => $"0x{HandleValue:X}";
    public string PermissionDisplay => IsAccessLimited ? "权限受限" : "可操作";
}
