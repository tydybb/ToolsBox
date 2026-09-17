using ToolsBox.Windows.NetworkTraffic;

namespace ToolsBox.Windows.Tests.NetworkTraffic;

public sealed class QosPolicyNameTests
{
    [Fact]
    public void ForPath_IsStableAndCaseInsensitive()
    {
        string first = QosPolicyName.ForPath(@"C:\Apps\Demo.exe");
        string second = QosPolicyName.ForPath(@"c:\apps\DEMO.exe");

        Assert.Equal(first, second);
        Assert.StartsWith(QosPolicyName.Prefix, first);
    }

    [Fact]
    public void ForPath_DiffersForDifferentPaths()
    {
        Assert.NotEqual(
            QosPolicyName.ForPath(@"C:\Apps\One.exe"),
            QosPolicyName.ForPath(@"C:\Apps\Two.exe"));
    }

    [Theory]
    [InlineData("BaoGeToolsBox-1234", true)]
    [InlineData("UserRule", false)]
    [InlineData("", false)]
    public void IsOwned_RequiresExactPrefix(string name, bool expected)
    {
        Assert.Equal(expected, QosPolicyName.IsOwned(name));
    }
}
