using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ToolsBox.App.WebResources;

namespace ToolsBox.App.Tests;

public class BrowserLibraryWindowTests
{
    [Fact]
    public Task HistoryDeletionPreservesFavoritesAndCorruptStoreDisablesOnlyLibrary() => WpfTestThread.RunAsync(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "ToolsBox-Library-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "BrowserLibrary.json");
            var store = new BrowserLibraryStore(path);
            store.AddFavorite("https://example.test/favorite", "收藏");
            store.RecordVisit("https://example.test/history", "历史");
            var window = new BrowserLibraryWindow(store, false);
            Assert.Equal(Visibility.Collapsed, ((DockPanel)window.FindName("RenamePanel")).Visibility);
            ((DataGrid)window.FindName("AddressesGrid")).SelectedIndex = 0;
            ((Button)window.FindName("DeleteAddressButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Empty(new BrowserLibraryStore(path).History);
            Assert.Single(new BrowserLibraryStore(path).Favorites);
            window.Close();
            File.WriteAllText(path, "{broken");
            var browser = new WebResourceWindow(directory);
            Assert.False(((Button)browser.FindName("FavoritesButton")).IsEnabled);
            Assert.False(((Button)browser.FindName("HistoryButton")).IsEnabled);
            Assert.True(((Button)browser.FindName("EnvironmentDetectionButton")).IsEnabled);
            browser.Close();
            Assert.Equal("{broken", File.ReadAllText(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FavoritesCanRenameDeleteAndOpenThroughControls() => WpfTestThread.RunAsync(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "ToolsBox-Library-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BrowserLibraryStore(Path.Combine(directory, "library.json"));
            store.AddFavorite("https://example.test/page", "原名称");
            store.AddFavorite("https://example.test/second", "第二条");
            var type = typeof(WebResourceWindow).Assembly.GetType("ToolsBox.App.WebResources.BrowserLibraryWindow");
            Assert.NotNull(type);
            var window = (Window)Activator.CreateInstance(type!, store, true)!;
            var grid = Assert.IsType<DataGrid>(window.FindName("AddressesGrid"));
            grid.SelectedIndex = 0;
            ((TextBox)window.FindName("FavoriteName")).Text = "新名称";
            ((Button)window.FindName("RenameButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("新名称", new BrowserLibraryStore(Path.Combine(directory, "library.json")).Favorites[0].Title);
            grid.SelectedIndex = 1;
            ((TextBox)window.FindName("FavoriteName")).Text = "改第二条";
            ((Button)window.FindName("RenameButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("https://example.test/second", ((BrowserAddress)grid.SelectedItem).Url);
            WpfTestSnapshot.SaveWindowContent(window, 720, 480, "browser-favorites.png");
            ((Button)window.FindName("DeleteAddressButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Single(store.Favorites);
            store.RemoveFavorite("https://example.test/page");
            window.Close();
            store.AddFavorite("https://example.test/open", "打开测试");
            window = (Window)Activator.CreateInstance(type!, store, true)!;
            ((DataGrid)window.FindName("AddressesGrid")).SelectedIndex = 0;
            ((Button)window.FindName("OpenAddressButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("https://example.test/open", type!.GetProperty("SelectedUrl")!.GetValue(window));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return Task.CompletedTask;
    });
}
