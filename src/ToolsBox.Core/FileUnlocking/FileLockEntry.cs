using System.ComponentModel;

namespace ToolsBox.Core.FileUnlocking;

public sealed record FileLockEntry(
    string LockedPath,
    long HandleValue,
    int ProcessId,
    string ProcessName,
    string ProcessPath,
    DateTimeOffset? ProcessStartedAt,
    bool IsAccessLimited) : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    // Event subscribers belong to the instance, not its record value or copies.
    private FileLockEntry(FileLockEntry original)
    {
        LockedPath = original.LockedPath;
        HandleValue = original.HandleValue;
        ProcessId = original.ProcessId;
        ProcessName = original.ProcessName;
        ProcessPath = original.ProcessPath;
        ProcessStartedAt = original.ProcessStartedAt;
        IsAccessLimited = original.IsAccessLimited;
        _isSelected = original.IsSelected;
    }

    public bool Equals(FileLockEntry? other) => other is not null &&
        LockedPath == other.LockedPath && HandleValue == other.HandleValue &&
        ProcessId == other.ProcessId && ProcessName == other.ProcessName &&
        ProcessPath == other.ProcessPath && ProcessStartedAt == other.ProcessStartedAt &&
        IsAccessLimited == other.IsAccessLimited && IsSelected == other.IsSelected;

    public override int GetHashCode() => HashCode.Combine(LockedPath, HandleValue, ProcessId,
        ProcessName, ProcessPath, ProcessStartedAt, IsAccessLimited, IsSelected);

    public string HandleDisplay => $"0x{HandleValue:X}";
    public string PermissionDisplay => IsAccessLimited ? "权限受限" : "可操作";
}
