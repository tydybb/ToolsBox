using ToolsBox.App;

namespace ToolsBox.App.Tests.Startup;

public class WebResourceStartupTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void BrowserModeRejectsAdministrator(bool elevated, bool expected)
    {
        var policy = typeof(App).Assembly.GetType("ToolsBox.App.WebResources.WebResourceLauncher");
        Assert.NotNull(policy);
        Assert.Equal(expected, policy!.GetMethod("CanRunBrowser")!.Invoke(null, [elevated]));
    }
}
