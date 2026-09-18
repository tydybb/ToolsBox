using System.IO;
using System.Xml.Linq;

namespace ToolsBox.App.Tests.Startup;

public sealed class FrameworkDependentPublishTests
{
    [Fact]
    public void PortableProfile_UsesNativeHostWithoutBundlingRuntime()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ToolsBox.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string projectPath = Path.Combine(root.FullName, "src", "ToolsBox.App");
        string profilePath = Path.Combine(projectPath, "Properties", "PublishProfiles", "PortableWinX64.pubxml");
        Assert.True(File.Exists(profilePath), "Missing repeatable portable publish profile.");
        var profile = XDocument.Load(profilePath);
        string Property(string name) => Assert.Single(profile.Descendants().Where(e => e.Name.LocalName == name)).Value;
        Assert.Equal("false", Property("SelfContained"));
        Assert.Equal("true", Property("PublishSingleFile"));
        Assert.Equal("true", Property("IncludeNativeLibrariesForSelfExtract"));
        Assert.Equal("true", Property("UseAppHost"));
        Assert.Equal("win-x64", Property("RuntimeIdentifier"));
        Assert.Equal("false", Property("PublishTrimmed"));
        var project = XDocument.Load(Path.Combine(projectPath, "ToolsBox.App.csproj"));
        Assert.Equal("WinExe", Assert.Single(project.Descendants().Where(e => e.Name.LocalName == "OutputType")).Value);
    }
}
