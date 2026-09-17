namespace ToolsBox.Core.FileUnlocking;

public static class FileUnlockSafetyPolicy
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "smss", "csrss", "wininit", "services", "lsass", "winlogon", "svchost"
    };

    public static bool CanOperate(FileLockEntry entry, int currentProcessId, out string reason)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.ProcessId is 0 or 4)
        {
            reason = "禁止操作 Windows 核心系统进程。";
            return false;
        }

        if (entry.ProcessId == currentProcessId)
        {
            reason = "禁止操作 ToolsBox 自身。";
            return false;
        }

        string processName = Path.GetFileNameWithoutExtension(entry.ProcessName);
        if (ProtectedNames.Contains(processName))
        {
            reason = $"禁止操作关键系统进程 {entry.ProcessName}。";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
