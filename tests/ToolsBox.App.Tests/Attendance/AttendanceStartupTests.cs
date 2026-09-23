using ToolsBox.App.Attendance;

namespace ToolsBox.App.Tests.Attendance;

public class AttendanceStartupTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void WindowsDisabledIsNotReportedAsEnabled(byte state)
    {
        byte[] approval=new byte[12];approval[0]=state;
        Assert.Contains("被 Windows 禁用",AttendanceStartup.Describe(AttendanceStartup.BuildCommand(@"C:\Tools\app.exe",null),approval,_=>true));
    }
    [Fact]
    public void MissingPathsAndUnknownStateAreExplicit()
    {
        string command=AttendanceStartup.BuildCommand(@"C:\Tools\app.exe",null);
        Assert.Contains("未注册",AttendanceStartup.Describe(null,null,_=>true));
        Assert.Contains("路径已失效",AttendanceStartup.Describe(command,null,_=>false));
        Assert.Contains("无法确认",AttendanceStartup.Describe(command,new byte[2],_=>true));
        Assert.Contains("未检测到",AttendanceStartup.Describe(command,null,_=>true));
        string hosted=AttendanceStartup.BuildCommand(@"C:\dotnet.exe",@"C:\Tools\app.dll");
        Assert.Contains("路径已失效",AttendanceStartup.Describe(hosted,null,p=>p.EndsWith(".exe")));
        Assert.Contains("未检测到",AttendanceStartup.Describe(hosted,null,_=>true));
    }
    [Fact]
    public void AttendanceLaunchRejectsDifferentOrUnknownDesktopUser()
    {
        ToolsBox.App.WebResources.WebResourceLauncher.ValidateAttendanceIdentity("same-user", "same-user");
        Assert.Throws<InvalidOperationException>(() => ToolsBox.App.WebResources.WebResourceLauncher.ValidateAttendanceIdentity("admin", "desktop"));
        Assert.Throws<InvalidOperationException>(() => ToolsBox.App.WebResources.WebResourceLauncher.ValidateAttendanceIdentity(null, null));
    }
    [Fact]
    public void CommandQuotesExecutableAndStartsFullToolbox()
    {
        Assert.Equal("\"C:\\My Tools\\宝哥工具箱.exe\"", AttendanceStartup.BuildCommand(@"C:\My Tools\宝哥工具箱.exe", null));
        Assert.Equal("\"C:\\dotnet.exe\" \"D:\\My Tools\\宝哥工具箱.dll\"", AttendanceStartup.BuildCommand(@"C:\dotnet.exe", @"D:\My Tools\宝哥工具箱.dll"));
        Assert.Throws<ArgumentException>(()=>AttendanceStartup.BuildCommand("bad\"path",null));
    }
}
