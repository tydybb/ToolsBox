using System.IO;
using System.Xml.Linq;

namespace ToolsBox.App.Tests.Startup;

public sealed class AdministratorStartupManifestTests
{
    [Fact]
    public void AppProject_UsesInvokerManifestWithMandatoryMainWindowElevationGate()
    {
        string root = FindRepositoryRoot();
        string manifestPath = Path.Combine(root, "src", "ToolsBox.App", "app.manifest");
        Assert.True(File.Exists(manifestPath), $"Missing application manifest: {manifestPath}");

        XDocument manifest = XDocument.Load(manifestPath);
        XElement executionLevel = Assert.Single(
            manifest.Descendants().Where(element => element.Name.LocalName == "requestedExecutionLevel"));
        Assert.Equal("asInvoker", executionLevel.Attribute("level")?.Value);
        Assert.Equal("false", executionLevel.Attribute("uiAccess")?.Value);

        XDocument project = XDocument.Load(Path.Combine(root, "src", "ToolsBox.App", "ToolsBox.App.csproj"));
        XElement applicationManifest = Assert.Single(
            project.Descendants().Where(element => element.Name.LocalName == "ApplicationManifest"));
        Assert.Equal("app.manifest", applicationManifest.Value);
        string startup = File.ReadAllText(Path.Combine(root, "src", "ToolsBox.App", "App.xaml.cs"));
        int gate = startup.IndexOf("AdministratorStartup.EnsureAdministrator", StringComparison.Ordinal);
        Assert.True(gate > startup.IndexOf("--archive-worker", StringComparison.Ordinal));
        Assert.True(gate < startup.IndexOf("MainWindow = new MainWindow", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public void MainWindowGate_OnlyAllowsAlreadyElevatedProcess(bool administrator, bool expectedContinue, bool expectedLaunch)
    {
        Type? type = typeof(App).Assembly.GetType("ToolsBox.App.Infrastructure.AdministratorStartup");
        Assert.NotNull(type);
        bool launched = false;
        Action<System.Diagnostics.ProcessStartInfo> launch = info =>
        {
            launched = true;
            Assert.True(info.UseShellExecute);
            Assert.Equal("runas", info.Verb);
        };
        bool result = (bool)type!.GetMethod("EnsureAdministrator")!.Invoke(null, [administrator, launch, "C:\\ToolsBox\\宝哥工具箱.exe", null])!;
        Assert.Equal(expectedContinue, result);
        Assert.Equal(expectedLaunch, launched);
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
