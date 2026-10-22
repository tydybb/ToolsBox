using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Xml.Linq;
using ToolsBox.App.Attendance;
using ToolsBox.App.Lottery;
using ToolsBox.App.NetworkTraffic;
using ToolsBox.App.Views;
using ToolsBox.App.WebResources;
using ToolsBox.Core.Attendance;
using ToolsBox.Core.FileUnlocking;
using ToolsBox.Core.NetworkTraffic;
using ToolsBox.Core.WebResources;
using ToolsBox.MediaDownloads;

namespace ToolsBox.App.Tests.Startup;

public sealed class WindowThemeCoverageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task EverySecondaryWindow_UsesComfortControlsAndLiveTheme(bool dark) => WpfTestThread.RunAsync(() =>
    {
        string scratch = Path.Combine(Path.GetTempPath(), "ToolsBox-WindowTheme-" + Guid.NewGuid().ToString("N"));
        var windows = CreateWindows(scratch);
        try
        {
            foreach (Window window in windows)
            {
                Apply(window, dark);
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                content.Measure(new Size(window.Width, double.IsNaN(window.Height) ? 450 : window.Height));
                int height = double.IsNaN(window.Height) ? (int)Math.Ceiling(content.DesiredSize.Height) : (int)window.Height;
                content.Arrange(new Rect(0, 0, window.Width, height));
                content.UpdateLayout();
                Assert.Equal(ColorFor(window, "Background"), ColorOf(window.Background));
                Assert.Equal(ColorFor(window, "Text"), ColorOf(window.Foreground));
                var actions = Descendants<Button>(content).Where(button => button.Content is string).ToArray();
                Assert.NotEmpty(actions);
                foreach (Button button in actions)
                {
                    button.ApplyTemplate();
                    Assert.True(button.Template.FindName("ButtonBorder", button) is not null,
                        $"{window.GetType().Name}: {button.Content} 未使用舒适按钮模板");
                }
                foreach (TextBox input in Descendants<TextBox>(content).Where(input => input.Visibility == Visibility.Visible))
                {
                    Assert.Equal(ColorFor(window, "Surface"), ColorOf(input.Background));
                    Assert.Equal(ColorFor(window, "Text"), ColorOf(input.Foreground));
                }
                WpfTestSnapshot.SaveWindowContent(window, (int)window.Width, height,
                    $"comfort-window-{window.GetType().Name}-{(dark ? "dark" : "light")}.png");
                Apply(window, !dark);
                content.UpdateLayout();
                Assert.Equal(ColorFor(window, "Background"), ColorOf(window.Background));
                Assert.Equal(ColorFor(window, "Text"), ColorOf(window.Foreground));
            }
        }
        finally { foreach (Window window in windows) window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DialogWithLateOwner_InheritsAndTracksOwnerTheme() => WpfTestThread.RunAsync(() =>
    {
        var owner = new Window { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        owner.Show();
        var dialog = new LotteryFavoriteNameDialog("幸运号") { Owner = owner };
        try
        {
            foreach (bool dark in new[] { true, false, true })
            {
                Apply(owner, dark);
                Assert.Equal(dark, dialog.Resources["ToolboxIsDarkTheme"]);
                Assert.Equal(ColorFor(owner, "Background"), ColorOf(dialog.Background));
                Assert.Equal(ColorFor(owner, "Text"), ColorOf(dialog.Foreground));
            }
        }
        finally { dialog.Close(); owner.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SourceInitialized_AdoptsTheOwnerAssignedAfterConstruction() => WpfTestThread.RunAsync(() =>
    {
        var owner = new Window { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        Apply(owner, true);
        owner.Show();
        var dialog = new LotteryFavoriteNameDialog("幸运号");
        try
        {
            dialog.Owner = owner;
            _ = new WindowInteropHelper(owner).EnsureHandle();
            _ = new WindowInteropHelper(dialog).EnsureHandle();
            Assert.Equal(true, dialog.Resources["ToolboxIsDarkTheme"]);
            Assert.Equal(ColorFor(owner, "Background"), ColorOf(dialog.Background));
        }
        finally { dialog.Close(); owner.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public void EveryAppOwnedSecondaryWindow_IsRegisteredAndHasNoHardcodedLightChrome()
    {
        string app = Path.Combine(FindRepository(), "src", "ToolsBox.App");
        string[] codeFiles = ["Attendance/AttendanceAccountPickerWindow.cs", "Attendance/AttendanceReminderWindow.cs",
            "Lottery/LotteryFavoriteNameDialog.cs", "Views/ProcessTerminationDialog.xaml.cs", "Views/BandwidthLimitWindow.xaml.cs",
            "WebResources/BrowserPopupWindow.xaml.cs", "WebResources/BrowserLibraryWindow.xaml.cs", "WebResources/EnvironmentWindow.xaml.cs",
            "WebResources/WebResourceWindow.xaml.cs", "WebResources/PlayerDownloadDialog.cs"];
        foreach (string file in codeFiles)
            Assert.Contains("ComfortAppearance.InitializeDialogWindow(this)", File.ReadAllText(Path.Combine(app, file)));
        Assert.Contains("ComfortAppearance.InitializeDialogWindow(_settings)", File.ReadAllText(Path.Combine(app, "Attendance/AttendanceHost.cs")));
        foreach (string file in codeFiles.Where(f => f.EndsWith(".xaml.cs")).Select(f => f[..^3]))
        {
            var document = XDocument.Load(Path.Combine(app, file));
            foreach (XAttribute attribute in document.Descendants().Attributes()
                .Where(a => a.Name.LocalName is "Foreground" or "Background" or "BorderBrush"))
                Assert.StartsWith("{", attribute.Value);
        }
    }

    private static Window[] CreateWindows(string scratch) =>
    [
        new AttendanceAccountPickerWindow([new DingTalkAccountPaths("C:\\演示账号_v3", "unused", null, "unused", null)],
            (_, _) => throw new InvalidOperationException("该视觉测试不得检查真实账号"), (_, _) => { }),
        new AttendanceReminderWindow(() => { }),
        new LotteryFavoriteNameDialog("我的幸运号"),
        new ProcessTerminationDialog("C:\\演示文件夹", [new FileLockEntry("example", 1, 123, "演示程序", "C:\\Example.exe", null, false)]),
        new BandwidthLimitWindow(new ApplicationTrafficItemViewModel(new ApplicationTrafficSnapshot("demo", "演示程序", "C:\\Example.exe", true, 0, 0, 0, 0, []), null)),
        new BrowserLibraryWindow(new BrowserLibraryStore(Path.Combine(scratch, "library.json")), true),
        new EnvironmentWindow(new ComponentManager(Path.Combine(scratch, "components"))),
        new WebResourceWindow(scratch),
        CreatePlayerDialog(scratch)
    ];

    private static void Apply(Window window, bool dark) => typeof(App).Assembly
        .GetType("ToolsBox.App.Infrastructure.ComfortAppearance")!.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static)!
        .Invoke(null, [window, dark]);

    private static Window CreatePlayerDialog(string scratch)
    {
        Type candidateType = typeof(App).Assembly.GetType("ToolsBox.App.WebResources.PlayerDownloadCandidate")!;
        object candidate = Activator.CreateInstance(candidateType, [0, 0L, new PlayerDocument { Url = "https://example.test" },
            new PlayerSnapshot(), new BoundPlayerMedia("演示视频", 60, [new PlayerMediaChoice("https://example.test/video.mp4", "原画", WebResourceKind.Video)])])!;
        Array candidates = Array.CreateInstance(candidateType, 1);
        candidates.SetValue(candidate, 0);
        return (Window)Activator.CreateInstance(typeof(App).Assembly.GetType("ToolsBox.App.WebResources.PlayerDownloadDialog")!, [candidates, scratch])!;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static Color ColorFor(Window window, string name) => ColorOf((Brush)window.Resources["Toolbox" + name + "Brush"]);
    private static Color ColorOf(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;
    private static string FindRepository()
    {
        for (var path = new DirectoryInfo(AppContext.BaseDirectory); path is not null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "ToolsBox.slnx"))) return path.FullName;
        throw new DirectoryNotFoundException("无法定位 ToolsBox.slnx。");
    }
}
