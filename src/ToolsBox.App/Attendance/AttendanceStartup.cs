using System.IO;
using Microsoft.Win32;

namespace ToolsBox.App.Attendance;

public static class AttendanceStartup
{
    public static string Describe(string? command, byte[]? approval, Func<string,bool> exists)
    {
        if(string.IsNullOrWhiteSpace(command))return "登录自启：未注册。请取消勾选后重新启用。";
        if(command.EndsWith(" --attendance-agent",StringComparison.Ordinal))return "登录自启：仍是旧版仅打卡后台模式，请取消勾选后重新启用，以启动完整工具箱。";
        var match=System.Text.RegularExpressions.Regex.Match(command,"^\"([^\"]+)\"(?: \\\"([^\"]+)\\\")?$");
        if(!match.Success)return "登录自启：启动命令异常，请取消勾选后重新启用。";
        if(!exists(match.Groups[1].Value) || match.Groups[2].Success&&!exists(match.Groups[2].Value))
            return "登录自启：程序路径已失效。请把 EXE 放在固定位置，再取消勾选并重新启用。";
        if(approval is not null)
        {
            if(approval.Length!=12)return "登录自启：Windows 状态无法确认，请打开系统启动设置核对。";
            uint state=System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(approval);
            if(state is 1 or 3 or 7)return "登录自启：已注册，但被 Windows 禁用。请打开系统启动设置启用 BaoGeToolsBox.Attendance（宝哥工具箱）。";
            if(state is not (2 or 6))return "登录自启：Windows 状态无法确认，请打开系统启动设置核对。";
        }
        return "登录自启：已注册完整工具箱，未检测到 Windows 禁用标记；登录后需确认管理员授权（尚不代表已通过重启验证）。";
    }
    public static string ReadStatus()
    {
        try
        {
            using var run=Registry.CurrentUser.OpenSubKey(RunKey);
            using var approved=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            return Describe(run?.GetValue(ValueName) as string,approved?.GetValue(ValueName) as byte[],File.Exists);
        }
        catch{return "登录自启：读取系统状态失败，请打开系统启动设置核对。";}
    }
    public const string ValueName = "BaoGeToolsBox.Attendance";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string BuildCommand(string executable, string? entryAssembly)
    {
        static string Quote(string value)
        {
            if(string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['"','\r','\n','\0']) >= 0 || !Path.IsPathFullyQualified(value))
                throw new ArgumentException("自启路径必须是完整的本地程序路径。");
            return "\"" + value + "\"";
        }
        string command = Quote(executable);
        if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))
            command += " " + Quote(entryAssembly ?? "");
        return command;
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true) ?? throw new IOException("无法修改当前用户登录启动项。");
        if(enabled) key.SetValue(ValueName,BuildCommand(Environment.ProcessPath!,Environment.GetCommandLineArgs()[0]),RegistryValueKind.String);
        else key.DeleteValue(ValueName,false);
    }
}
