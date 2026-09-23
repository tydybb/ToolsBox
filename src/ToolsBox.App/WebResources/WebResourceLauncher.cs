using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ToolsBox.App.WebResources;

public static class WebResourceLauncher
{
    // Request the access rights Windows permits on the duplicated shell token.
    // The narrow QUERY/DUPLICATE/ASSIGN_PRIMARY mask fails during process/profile
    // creation on Windows (ERROR_ACCESS_DENIED). This does not elevate the token.
    private const uint MaximumAllowedTokenAccess = 0x02000000;
    public static bool CanRunBrowser(bool administrator) => !administrator;

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static Process Launch()
    {
        var arguments = new List<string>();
        arguments.Add("--web-resources");
        using var parent = Process.GetCurrentProcess();
        arguments.Add(parent.Id.ToString());
        arguments.Add(parent.StartTime.ToUniversalTime().Ticks.ToString());
        return LaunchOrdinaryTool(arguments);
    }

    public static Process LaunchAttendance() => LaunchOrdinaryTool(["--attendance-agent"], requireSameUser: true);

    public static void ValidateAttendanceIdentity(string? currentSid, string? desktopSid)
    {
        if (string.IsNullOrEmpty(currentSid) || !string.Equals(currentSid, desktopSid, StringComparison.Ordinal))
            throw new InvalidOperationException("请使用与当前 Windows 桌面相同的账号启用打卡提醒，不能使用其他管理员账号代为启用。");
    }

    private static Process LaunchOrdinaryTool(List<string> arguments, bool requireSameUser = false)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位程序。");
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            arguments.Insert(0,Path.GetFullPath(Environment.GetCommandLineArgs()[0]));
        if (!IsAdministrator())
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var value in arguments) start.ArgumentList.Add(value);
            return Process.Start(start) ?? throw new InvalidOperationException("无法启动浏览窗口。");
        }

        var shell = GetShellWindow();
        if (shell == IntPtr.Zero) throw new InvalidOperationException("找不到普通权限桌面，无法安全启动浏览窗口。");
        GetWindowThreadProcessId(shell, out uint shellId);
        using var process = OpenProcess(0x1000, false, shellId);
        if (process.IsInvalid) throw NativeFailure("打开桌面进程");
        if (!OpenProcessToken(process, 0x0002 | 0x0008, out var token)) throw NativeFailure("读取普通用户令牌");
        using (token)
        {
            if (requireSameUser)
            {
                using var currentIdentity = WindowsIdentity.GetCurrent();
                using var desktopIdentity = new WindowsIdentity(token.DangerousGetHandle());
                ValidateAttendanceIdentity(currentIdentity.User?.Value, desktopIdentity.User?.Value);
            }
            if (!DuplicateTokenEx(token, MaximumAllowedTokenAccess, IntPtr.Zero, 2, 1, out var primary)) throw NativeFailure("复制普通用户令牌");
            using (primary)
            {
                if (!CreateEnvironmentBlock(out var environment, primary, false)) throw NativeFailure("创建普通用户环境");
                try
                {
                    var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>(), desktop = "winsta0\\default" };
                    var command = new StringBuilder(string.Join(" ", new[] { exe }.Concat(arguments).Select(Quote)));
                    if (!CreateProcessWithTokenW(primary, 1, exe, command, 0x400, environment,
                            Path.GetDirectoryName(exe)!, ref startup, out var created)) throw NativeFailure("启动普通权限进程");
                    try { return Process.GetProcessById((int)created.processId); }
                    finally { CloseHandle(created.thread); CloseHandle(created.process); }
                }
                finally { DestroyEnvironmentBlock(environment); }
            }
        }
    }

    private static Win32Exception NativeFailure(string stage)
    {
        int code = Marshal.GetLastWin32Error();
        return new Win32Exception(code, $"{stage}失败（Windows 错误 {code}）。");
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb; public string? reserved; public string? desktop; public string? title;
        public int x, y, xSize, ySize, xCount, yCount, fill, flags;
        public short show, reservedSize; public IntPtr reserved2, input, output, error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr process, thread; public uint processId, threadId; }
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateTokenEx(SafeAccessTokenHandle existing, uint access, IntPtr attributes, int level, int type, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessWithTokenW(SafeAccessTokenHandle token, uint logonFlags, string application, StringBuilder command, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation info);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool CreateEnvironmentBlock(out IntPtr environment, SafeAccessTokenHandle token, bool inherit);
    [DllImport("userenv.dll")] private static extern bool DestroyEnvironmentBlock(IntPtr environment);
}
