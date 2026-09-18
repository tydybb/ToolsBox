using System.IO;
using System.Xml.Linq;

namespace ToolsBox.App.Tests.Startup;

public sealed class AdministratorStartupManifestTests
{
    [Fact]
    public void AppProject_EmbedsManifestThatRequiresAdministrator()
    {
        string root = FindRepositoryRoot();
        string manifestPath = Path.Combine(root, "src", "ToolsBox.App", "app.manifest");
        Assert.True(File.Exists(manifestPath), $"Missing application manifest: {manifestPath}");

        XDocument manifest = XDocument.Load(manifestPath);
        XElement executionLevel = Assert.Single(
            manifest.Descendants().Where(element => element.Name.LocalName == "requestedExecutionLevel"));
        Assert.Equal("requireAdministrator", executionLevel.Attribute("level")?.Value);
        Assert.Equal("false", executionLevel.Attribute("uiAccess")?.Value);

        XDocument project = XDocument.Load(Path.Combine(root, "src", "ToolsBox.App", "ToolsBox.App.csproj"));
        XElement applicationManifest = Assert.Single(
            project.Descendants().Where(element => element.Name.LocalName == "ApplicationManifest"));
        Assert.Equal("app.manifest", applicationManifest.Value);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ToolsBox.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the ToolsBox repository root.");
    }
}
