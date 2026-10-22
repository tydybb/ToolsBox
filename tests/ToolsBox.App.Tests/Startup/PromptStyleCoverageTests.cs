using System.IO;
using System.Text.RegularExpressions;

namespace ToolsBox.App.Tests.Startup;

public sealed class PromptStyleCoverageTests
{
    [Fact]
    public void StandaloneApp_LoadsComfortControlTemplates()
    {
        string appXaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "ToolsBox.App", "App.xaml"));
        Assert.Contains("Themes/ComfortControls.xaml", appXaml);
    }

    [Fact]
    public void AppOwnedPrompts_DoNotFallBackToSystemMessageBoxes()
    {
        string root = FindRepositoryRoot();
        string app = Path.Combine(root, "src", "ToolsBox.App");
        var omissions = new List<string>();
        foreach (string file in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(app, file);
            if (relative.StartsWith("obj" + Path.DirectorySeparatorChar)
                || relative.StartsWith("bin" + Path.DirectorySeparatorChar)) continue;
            if (Regex.IsMatch(File.ReadAllText(file), @"(?<!Comfort)\bMessageBox\s*\.\s*Show\s*\("))
                omissions.Add(relative);
        }
        Assert.Empty(omissions);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "ToolsBox.App", "ToolsBox.App.csproj")))
                return directory.FullName;
        throw new DirectoryNotFoundException("未找到工具箱源码目录，无法审计弹窗样式。");
    }
}
