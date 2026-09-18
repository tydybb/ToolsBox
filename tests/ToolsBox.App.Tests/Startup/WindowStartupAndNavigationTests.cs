using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

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
            var window = new MainWindow();
            try
            {
                var vm = Assert.IsType<MainViewModel>(window.DataContext);
                var ports = Assert.IsAssignableFrom<ToggleButton>(window.FindName("PortNavigation"));
                var files = Assert.IsAssignableFrom<ToggleButton>(window.FindName("FileNavigation"));
                var network = Assert.IsAssignableFrom<ToggleButton>(window.FindName("NetworkNavigation"));
                ToggleButton[] buttons = [ports, files, network];
                object[] pages = [vm.PortMonitor, vm.FileUnlocker, vm.NetworkTraffic];
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                foreach (int index in new[] { 0, 2, 1, 0 })
                {
                    buttons[index].Command.Execute(null);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Same(pages[index], vm.CurrentTool);
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
}
