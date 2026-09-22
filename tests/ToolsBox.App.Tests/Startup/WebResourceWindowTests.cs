using System.Windows;
using System.Windows.Controls;
using ToolsBox.App;

namespace ToolsBox.App.Tests.Startup;

public class WebResourceWindowTests
{
    [Fact]
    public Task SelectionOnlyChangesVisibleRowsAndRetainsHiddenChoices() => WpfTestThread.RunAsync(() =>
    {
        var window = new ToolsBox.App.WebResources.WebResourceWindow();
        try
        {
            var grid = (DataGrid)window.FindName("ResourcesGrid");
            var rows = (System.Collections.ObjectModel.ObservableCollection<ToolsBox.App.WebResources.WebResourceRow>)grid.ItemsSource;
            var image = new ToolsBox.App.WebResources.WebResourceRow("https://example.test/a.png", "https://example.test", ToolsBox.Core.WebResources.WebResourceKind.Image, null);
            var video = new ToolsBox.App.WebResources.WebResourceRow("https://example.test/a.mp4", "https://example.test", ToolsBox.Core.WebResources.WebResourceKind.Video, null);
            rows.Add(image); rows.Add(video);
            var selected = image.GetType().GetProperty("IsSelected");
            Assert.NotNull(selected);
            var header = Assert.IsType<CheckBox>(window.FindName("SelectAllResources"));
            Assert.False(header.IsChecked);
            void Invoke(string name) => window.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
            ((ComboBox)window.FindName("KindFilter")).SelectedIndex = 1;
            Invoke("SelectAll");
            Assert.Equal(true, selected.GetValue(image)); Assert.Equal(false, selected.GetValue(video));
            Assert.True(header.IsChecked);
            ((ComboBox)window.FindName("KindFilter")).SelectedIndex = 0;
            Assert.Null(header.IsChecked);
            header.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.True(header.IsChecked);
            header.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.False(header.IsChecked);
            selected.SetValue(image, true);
            ((ComboBox)window.FindName("KindFilter")).SelectedIndex = 2;
            Invoke("SelectAll"); Invoke("ClearSelection");
            Assert.Equal(true, selected.GetValue(image)); Assert.Equal(false, selected.GetValue(video));
            WpfTestSnapshot.SaveWindowContent(window, 1000, 700, "web-resources-filter-selection.png");
            rows.Clear();
            Assert.False(header.IsChecked); Assert.False(header.IsEnabled);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RetryCannotEnqueueDuplicateActiveResource() => WpfTestThread.RunAsync(() =>
    {
        var window = new ToolsBox.App.WebResources.WebResourceWindow();
        var type = window.GetType();
        var tasks = (System.Collections.ObjectModel.ObservableCollection<ToolsBox.App.WebResources.WebDownloadRow>)type.GetField("_tasks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
        var resource = new ToolsBox.App.WebResources.WebResourceRow("https://example.test/a.mp4", "https://example.test", ToolsBox.Core.WebResources.WebResourceKind.Video, null);
        tasks.Add(new(resource, System.IO.Path.GetTempPath()));
        type.GetMethod("StartDownload", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, [new ToolsBox.App.WebResources.WebDownloadRow(resource, System.IO.Path.GetTempPath())]);
        Assert.Single(tasks);
        window.Close();
        return Task.CompletedTask;
    });
    [Fact]
    public Task RepeatedCloseCannotSkipCleanup() => WpfTestThread.RunAsync(() =>
    {
        var window = new ToolsBox.App.WebResources.WebResourceWindow();
        var type = window.GetType();
        type.GetField("_closing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, true);
        var args = new System.ComponentModel.CancelEventArgs();
        type.GetMethod("OnClosing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(window, [window, args]);
        Assert.True(args.Cancel);
        type.GetField("_closing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, false);
        window.Close();
        return Task.CompletedTask;
    });
    [Fact]
    public Task WindowContainsBrowserResourcesAndTasksWithoutNavigating() => WpfTestThread.RunAsync(() =>
    {
        var type = typeof(App).Assembly.GetType("ToolsBox.App.WebResources.WebResourceWindow");
        Assert.NotNull(type);
        var window = (Window)Activator.CreateInstance(type!)!;
        Assert.NotNull(window.FindName("Browser"));
        Assert.IsType<DataGrid>(window.FindName("ResourcesGrid"));
        Assert.IsType<DataGrid>(window.FindName("TasksGrid"));
        Assert.IsType<Button>(window.FindName("EnvironmentDetectionButton"));
        Assert.IsType<Button>(window.FindName("AddFavoriteButton"));
        Assert.IsType<Button>(window.FindName("FavoritesButton"));
        Assert.IsType<Button>(window.FindName("HistoryButton"));
        Assert.IsType<GridSplitter>(window.FindName("BrowserHeightSplitter"));
        Assert.IsType<Button>(window.FindName("PopOutBrowserButton"));
        Assert.Null(window.FindName("ExportThunderButton"));
        Assert.Null(window.FindName("CancelExportButton"));
        Assert.Null(type.GetMethod("ExportThunder", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        Assert.Null(type.Assembly.GetType("ToolsBox.App.WebResources.DirectLinkExporter"));
        Assert.Null(window.FindName("InstallComponentsButton"));
        var filter = Assert.IsType<ComboBox>(window.FindName("KindFilter"));
        Assert.Equal(32d, filter.Height);
        Assert.Equal(VerticalAlignment.Center, filter.VerticalContentAlignment);
        WpfTestSnapshot.SaveWindowContent(window, 1280, 860, "web-resources-window.png");
        var splitter = (GridSplitter)window.FindName("BrowserHeightSplitter");
        var splitGrid = (Grid)splitter.Parent;
        double originalHeight = splitGrid.RowDefinitions[0].ActualHeight;
        var peer = new System.Windows.Automation.Peers.GridSplitterAutomationPeer(splitter);
        var transform = (System.Windows.Automation.Provider.ITransformProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Transform);
        transform.Move(0, -30);
        ((FrameworkElement)window.Content).UpdateLayout();
        Assert.InRange(originalHeight - splitGrid.RowDefinitions[0].ActualHeight, 29, 31);
        window.Close();
        return Task.CompletedTask;
    });
}
