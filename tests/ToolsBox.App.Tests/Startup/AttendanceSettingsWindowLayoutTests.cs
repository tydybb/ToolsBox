using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ToolsBox.App.Attendance;

namespace ToolsBox.App.Tests.Startup;

public sealed class AttendanceSettingsWindowLayoutTests
{
    [Theory]
    [InlineData(720, 600, false)]
    [InlineData(620, 450, true)]
    public Task ExpandedSettings_ActionsAreReachableWithinTheRealClientArea(int width, int height, bool dark) =>
        WpfTestThread.RunAsync(() =>
        {
            var panel = new AttendancePanel(new AttendanceStore(Path.Combine(Path.GetTempPath(), "ToolsBox-SettingsVisual-" + Guid.NewGuid().ToString("N"))),
                _ => throw new InvalidOperationException("不得修改启动设置"),
                () => throw new InvalidOperationException("不得启动打卡进程"), () => false,
                () => throw new InvalidOperationException("不得读取钉钉目录"));
            // IsChecked only changes visual expansion. Do not click, show, or raise Loaded: no consent, polling or startup detection.
            Assert.IsType<CheckBox>(panel.FindName("AttendanceEnabled")).IsChecked = true;
            Assert.IsType<TextBlock>(panel.FindName("AttendanceStartupStatus")).Text =
                "登录自启：已注册完整工具箱，未检测到 Windows 禁用标记；登录后需确认管理员授权（尚不代表已通过重启验证）。";
            var status = Assert.IsType<TextBlock>(panel.FindName("AttendanceStatus"));
            status.Text = "未能确认今日打卡，读取失败时仍可手动确认。\n最后检查：09:25:00";
            Window window = CreateSettingsHost(panel);
            try
            {
                window.Width = width; window.Height = height;
                Type appearance = typeof(App).Assembly.GetType("ToolsBox.App.Infrastructure.ComfortAppearance")!;
                appearance.GetMethod("InitializeDialogWindow")!.Invoke(null, [window]);
                IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
                Assert.True(GetClientRect(handle, out NativeRect client));
                appearance.GetMethod("Apply")!.Invoke(null, [window, dark]);
                var root = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                int clientWidth = client.Right - client.Left, clientHeight = client.Bottom - client.Top;
                root.Measure(new Size(clientWidth, clientHeight));
                root.Arrange(new Rect(0, 0, clientWidth, clientHeight)); root.UpdateLayout();
                Button[] actions = Descendants<Button>(panel).Where(button => button.Content is "保存设置" or "立即检查" or "我已打卡").ToArray();
                Assert.Equal(3, actions.Length);
                var scroll = Assert.IsType<ScrollViewer>(root);
                Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
                if (height == 450) Assert.True(scroll.ExtentHeight > scroll.ViewportHeight);
                scroll.ScrollToEnd(); scroll.UpdateLayout();
                foreach (Button action in actions)
                {
                    Point position = action.TransformToAncestor(scroll).Transform(new Point());
                    Assert.True(action.ActualHeight >= 38);
                    Assert.InRange(position.Y, -0.5, scroll.ActualHeight);
                    Assert.True(position.Y + action.ActualHeight <= scroll.ActualHeight + 0.5);
                }
                Assert.True(status.TransformToAncestor(scroll).Transform(new Point()).Y + status.ActualHeight <= scroll.ActualHeight + 0.5);
                WpfTestSnapshot.SaveWindowContent(window, clientWidth, clientHeight,
                    $"comfort-attendance-settings-{width}-{height}-{(dark ? "dark" : "light")}.png");
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        });

    private static Window CreateSettingsHost(AttendancePanel panel)
    {
        MethodInfo? factory = typeof(AttendanceHost).GetMethod("CreateSettingsWindow", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(factory);
        return (Window)factory.Invoke(null, [panel])!;
    }

    [Fact]
    public void ActualSettingsRoute_UsesTheVerifiedWindowFactory()
    {
        string source = File.ReadAllText(Path.Combine(Repository(), "src", "ToolsBox.App", "Attendance", "AttendanceHost.cs"));
        Assert.Contains("_settings=CreateSettingsWindow(new AttendancePanel());", source);
        Assert.Contains("ComfortAppearance.InitializeDialogWindow(_settings);", source);
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
    private static string Repository()
    {
        for (var path = new DirectoryInfo(AppContext.BaseDirectory); path is not null; path = path.Parent)
            if (File.Exists(Path.Combine(path.FullName, "ToolsBox.slnx"))) return path.FullName;
        throw new DirectoryNotFoundException();
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr handle, out NativeRect rect);
}
