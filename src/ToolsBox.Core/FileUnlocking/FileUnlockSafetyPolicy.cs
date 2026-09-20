namespace ToolsBox.Core.FileUnlocking;

public static class FileUnlockSafetyPolicy
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "smss", "csrss", "wininit", "services", "lsass", "winlogon", "svchost",
        "explorer", "dwm", "sihost", "ShellExperienceHost", "StartMenuExperienceHost"
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

        if (entry.ProcessStartedAt is null)
        {
            reason = "缺少可验证的进程身份，请使用管理员扫描重新获取占用信息。";
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
