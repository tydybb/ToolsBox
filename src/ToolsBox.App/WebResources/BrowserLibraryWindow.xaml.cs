using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ToolsBox.App.WebResources;

public partial class BrowserLibraryWindow : Window
{
    private readonly BrowserLibraryStore _store;
    private readonly bool _favorites;
    public string? SelectedUrl { get; private set; }

    public BrowserLibraryWindow(BrowserLibraryStore store, bool favorites)
    {
        _store = store; _favorites = favorites;
        InitializeComponent();
        Title = Heading.Text = favorites ? "收藏夹" : "历史记录";
        Explanation.Text = favorites ? "收藏保存在本机。选中后可打开、重命名或删除；地址可能含敏感参数，请勿公开分享。" : "最近访问优先，同一地址去重，最多保留 500 条。删除或清空历史不会清除网站登录状态。";
        RenamePanel.Visibility = favorites ? Visibility.Visible : Visibility.Collapsed;
        ClearHistoryButton.Visibility = favorites ? Visibility.Collapsed : Visibility.Visible;
        Refresh();
    }
    private void Refresh()
    {
        var selectedUrl = (AddressesGrid.SelectedItem as BrowserAddress)?.Url;
        int selectedIndex = AddressesGrid.SelectedIndex;
        AddressesGrid.ItemsSource = _favorites ? _store.Favorites : _store.History;
        var same = AddressesGrid.Items.OfType<BrowserAddress>().FirstOrDefault(row => row.Url == selectedUrl);
        if (same != null) AddressesGrid.SelectedItem = same;
        else AddressesGrid.SelectedIndex = AddressesGrid.Items.Count > 0 ? Math.Clamp(selectedIndex, 0, AddressesGrid.Items.Count - 1) : -1;
        UpdateButtons();
        LibraryStatus.Text = $"共 {AddressesGrid.Items.Count} 条 · 仅保存在当前 Windows 用户下";
    }
    private void UpdateButtons()
    {
        var row = AddressesGrid.SelectedItem as BrowserAddress;
        OpenAddressButton.IsEnabled = DeleteAddressButton.IsEnabled = RenameButton.IsEnabled = row != null;
        FavoriteName.Text = row?.Title ?? "";
        ClearHistoryButton.IsEnabled = _store.History.Count > 0;
    }
    private void AddressSelected(object sender, SelectionChangedEventArgs e) => UpdateButtons();
    private void OpenSelected(object sender, RoutedEventArgs e)
    { if (AddressesGrid.SelectedItem is BrowserAddress row) { SelectedUrl = row.Url; Close(); } }
    private void OpenDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(AddressesGrid, source) is DataGridRow)
            OpenSelected(sender, e);
    }
    private void Change(Action action)
    {
        try { action(); Refresh(); }
        catch { LibraryStatus.Text = "保存失败，原记录已保留。请检查磁盘空间或数据目录权限后重试。"; }
    }
    private void RenameFavorite(object sender, RoutedEventArgs e)
    {
        if (!_favorites || AddressesGrid.SelectedItem is not BrowserAddress row) return;
        if (string.IsNullOrWhiteSpace(FavoriteName.Text)) { LibraryStatus.Text = "请输入收藏名称。"; return; }
        Change(() => _store.RenameFavorite(row.Url, FavoriteName.Text));
    }
    private void DeleteSelected(object sender, RoutedEventArgs e)
    {
        if (AddressesGrid.SelectedItem is not BrowserAddress row) return;
        Change(() => { if (_favorites) _store.RemoveFavorite(row.Url); else _store.RemoveHistory(row.Url); });
    }
    private void ClearHistory(object sender, RoutedEventArgs e)
    {
        if (_favorites || MessageBox.Show(this, "清空全部历史记录？收藏和网站登录状态将保留。", "清空历史", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        Change(_store.ClearHistory);
    }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
