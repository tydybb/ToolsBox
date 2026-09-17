using ToolsBox.Windows.NetworkTraffic;

namespace ToolsBox.Windows.Tests.NetworkTraffic;

public sealed class PowerShellQosCommandRunnerTests
{
    [Fact]
    public void CreateStartInfo_PutsUserValuesInEnvironmentNotScript()
    {
        const string path = "C:\\Apps\\demo'; Remove-Item C:\\Important; '.exe";
        const string name = "BaoGeToolsBox-safe";

        var startInfo = PowerShellQosCommandRunner.CreateStartInfo(
            QosCommandOperation.Create,
            name,
            path,
            8_000_000);
        string arguments = string.Join(" ", startInfo.ArgumentList);

        Assert.DoesNotContain(path, arguments, StringComparison.Ordinal);
        Assert.DoesNotContain(name, arguments, StringComparison.Ordinal);
        Assert.Equal(path, startInfo.Environment[PowerShellQosCommandRunner.PathEnvironmentName]);
        Assert.Equal(name, startInfo.Environment[PowerShellQosCommandRunner.NameEnvironmentName]);
        Assert.Equal("8000000", startInfo.Environment[PowerShellQosCommandRunner.RateEnvironmentName]);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
    }
}
