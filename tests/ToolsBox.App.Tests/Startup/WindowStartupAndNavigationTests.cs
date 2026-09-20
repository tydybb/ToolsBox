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
            var app = new App();
            app.InitializeComponent();
            // Async helper startup must not leave an automatic window launch queued by WPF.
            Assert.Null(app.StartupUri);
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
                ToggleButton[] buttons = [ports, files, network, countdown];
                object[] pages = [vm.PortMonitor, vm.FileUnlocker, vm.NetworkTraffic,
                    vm.GetType().GetProperty("WorkCountdown")!.GetValue(vm)!];
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                foreach (int index in new[] { 0, 2, 1, 3, 0, 3 })
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
            }
            finally
            {
                window.Close();
            }
        });

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    private sealed class EmptyCountdownStore : ToolsBox.App.WorkCountdown.ICountdownStateStore
    {
        public ToolsBox.App.WorkCountdown.CountdownState? Load() => null;
        public void Save(ToolsBox.App.WorkCountdown.CountdownState state) { }
        public void Clear() { }
    }
}
