using System.IO;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace ToolsBox.App.Tests.Startup;

public sealed class WindowStartupAndNavigationTests
{
    [Fact]
    public Task StartupResources_DoNotOpenHelperWindow_AndNavigationTracksCurrentPage() =>
        WpfTestThread.RunAsync(async () =>
        {
            // 禁止在测试进程 new App()：阶段探针证实，任何 Application 实例一经创建（无论其
            // Dispatcher 存活、单例已反射清空还是线程已退出），都会静默毒化本进程随后其他测试
            // 线程的 Window.Show()——AttendanceAccountPickerTests 的 owner 拿不到窗口源、设置
            // Owner 抛“之前未显示”；从不创建则一切正常。
            // Async helper startup must not leave an automatic window launch queued by WPF：
            // 等价断言改为直接检查 App.xaml 未声明 StartupUri（StartupUri 只可能来自该声明）。
            AssertAppXamlDeclaresNoStartupUri();
            using var countdownModel = new ToolsBox.App.WorkCountdown.WorkCountdownViewModel(
                () => new DateTime(2026, 9, 18, 9, 0, 0), new EmptyCountdownStore(), false);
            var window = new MainWindow(countdownModel, _ => { });
            try
            {
                IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
                var vm = Assert.IsType<MainViewModel>(window.DataContext);
                var ports = Assert.IsAssignableFrom<ToggleButton>(window.FindName("PortNavigation"));
                var files = Assert.IsAssignableFrom<ToggleButton>(window.FindName("FileNavigation"));
                var fileLabel = Assert.IsType<System.Windows.Controls.StackPanel>(files.Content)
                    .Children.OfType<System.Windows.Controls.TextBlock>().Last();
                Assert.Equal("文件解除占用", fileLabel.Text);
                var network = Assert.IsAssignableFrom<ToggleButton>(window.FindName("NetworkNavigation"));
                Assert.Null(window.FindName("ArchiveNavigation"));
                Assert.Null(vm.GetType().GetProperty("ArchiveRecovery"));
                var countdown = Assert.IsAssignableFrom<ToggleButton>(window.FindName("CountdownNavigation"));
                var web = Assert.IsAssignableFrom<ToggleButton>(window.FindName("WebResourcesNavigation"));
                Assert.Same(ports.Style, web.Style);
                ToggleButton[] buttons = [ports, files, network, countdown, web];
                object[] pages = [vm.PortMonitor, vm.FileUnlocker, vm.NetworkTraffic,
                    vm.GetType().GetProperty("WorkCountdown")!.GetValue(vm)!,
                    vm.GetType().GetProperty("WebResources")!.GetValue(vm)!];
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                foreach (int index in new[] { 0, 2, 1, 3, 4, 0, 4, 3 })
                {
                    buttons[index].Command.Execute(null);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Same(pages[index], vm.CurrentTool);
                    // Explorer requires the shell file-drop window style, not just WPF AllowDrop.
                    Assert.Equal(index == 1, (GetWindowLong(handle, -20) & 0x10 /* WS_EX_ACCEPTFILES */) != 0);
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        Assert.Equal(i == index, buttons[i].IsChecked);
                        Assert.Equal(i == index ? "#FF246BFD" : "#FF263442",
                            Assert.IsType<SolidColorBrush>(buttons[i].Background).Color.ToString());
                    }
                }
                WpfTestSnapshot.SaveWindowContent(window, 1280, 760, "main-navigation-unified.png");
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>从仓库源码定位 src/ToolsBox.App/App.xaml 并断言根元素未声明 StartupUri（无需创建 Application 实例）。</summary>
    private static void AssertAppXamlDeclaresNoStartupUri()
    {
        string? directory = AppContext.BaseDirectory;
        string? path = null;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            string candidate = Path.Combine(directory, "src", "ToolsBox.App", "App.xaml");
            if (File.Exists(candidate)) { path = candidate; break; }
            directory = Path.GetDirectoryName(directory);
        }
        Assert.NotNull(path);
        var document = System.Xml.Linq.XDocument.Load(path!);
        Assert.Null(document.Root?.Attribute("StartupUri"));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    private sealed class EmptyCountdownStore : ToolsBox.App.WorkCountdown.ICountdownStateStore
    {
        public ToolsBox.App.WorkCountdown.CountdownState? Load() => null;
        public void Save(ToolsBox.App.WorkCountdown.CountdownState state) { }
        public void Clear() { }
    }
}
