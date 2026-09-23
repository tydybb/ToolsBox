using System.IO;
using ToolsBox.App.Infrastructure;

namespace ToolsBox.App.Tests.Attendance;

public class ToolboxExitChannelTests
{
    [Fact]
    public void ExitReachesExistingRolesButNotNewLaunchOrDifferentSession()
    {
        string root=Path.Combine(Path.GetTempPath(),"ToolsBox-exit-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            var main=new ToolboxExitChannel(root,1);var agent=new ToolboxExitChannel(root,1);var browser=new ToolboxExitChannel(root,1);
            var other=new ToolboxExitChannel(root,2);
            agent.RequestExit();
            Assert.True(main.ShouldExit());Assert.True(agent.ShouldExit());Assert.True(browser.ShouldExit());
            Assert.False(other.ShouldExit());Assert.False(new ToolboxExitChannel(root,1).ShouldExit());
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
