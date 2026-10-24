using System.Net.Sockets;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ToolsBox.App.Ports;
using ToolsBox.App.Views;
using ToolsBox.Core.Ports;

namespace ToolsBox.App.Tests.Ports;

public sealed class PortMonitorSearchViewTests
{
    [Theory]
    [InlineData(false, 744)]
    [InlineData(true, 744)]
    [InlineData(false, 1060)]
    [InlineData(true, 1060)]
    public Task SearchSelectorHasFiveOptionsAndKeepsTheToolbarUsable(bool dark, int width) => WpfTestThread.RunAsync(async () =>
    {
        var provider = new Provider();
        using var vm = new PortMonitorViewModel(provider);
        await vm.InitializeAsync();
        var view = new PortMonitorView { DataContext = vm };
        view.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/宝哥工具箱;component/Themes/ComfortControls.xaml", UriKind.Relative)
        });
        var host = new Window
        {
            Content = view, Resources = view.Resources, ShowActivated = false, ShowInTaskbar = false,
            FontSize = 13, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei")
        };
        typeof(App).Assembly.GetType("ToolsBox.App.Infrastructure.ComfortAppearance", true)!
            .GetMethod("Apply", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [host, dark]);
        try
        {
            view.Measure(new Size(width, 560));
            view.Arrange(new Rect(0, 0, width, 560));
            view.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var selector = Assert.IsType<ComboBox>(view.FindName("PortSearchField"));
            Assert.Equal(5, selector.Items.Count);
            Assert.Equal(new[] { "全部", "端口", "IP 地址", "进程 PID", "进程名称" }, selector.Items.Cast<object>()
                .Select(item => (string)item.GetType().GetProperty("Label")!.GetValue(item)!).ToArray());
            selector.SelectedIndex = 1;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal("Port", vm.GetType().GetProperty("SearchField")!.GetValue(vm)!.ToString());
            var search = Assert.IsType<TextBox>(view.FindName("PortSearchInput"));
            Assert.Contains("端口", (string)search.ToolTip);
            Assert.True(search.ActualWidth >= 120, $"Search input too narrow: {search.ActualWidth}");
            var autoButton = Assert.IsType<Button>(view.FindName("PortToggleRefreshButton"));
            var refreshButton = Assert.IsType<Button>(view.FindName("PortRefreshButton"));
            Assert.Same(vm.TogglePauseCommand, autoButton.Command);
            Assert.Same(vm.RefreshCommand, refreshButton.Command);
            Assert.True(autoButton.TranslatePoint(new Point(autoButton.ActualWidth, 0), view).X <= width);
            Assert.True(refreshButton.TranslatePoint(new Point(refreshButton.ActualWidth, 0), view).X <= width);
            Assert.Equal(0, provider.Calls);
            Assert.True(vm.IsPaused);
            Assert.False(view.IsLoaded);
        }
        finally { host.Close(); }
    });

    private sealed class Provider : IPortSnapshotProvider
    {
        public int Calls;
        public Task<IReadOnlyList<PortEntry>> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<PortEntry>>(
                [new("TCP", AddressFamily.InterNetwork, "127.0.0.1", 80, "", null, "Listen", 42, "Synthetic")]);
        }
    }
}
