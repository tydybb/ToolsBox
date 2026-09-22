using System.IO;
using System.Windows;
using ToolsBox.Core.WebResources;

namespace ToolsBox.App.WebResources;

public partial class WebResourceWindow
{
    private BrowserLibraryStore? _library;
    private bool _libraryLoadFailed;
    private void InitializeLibrary()
    {
        try { _library = new BrowserLibraryStore(Path.Combine(_dataDirectory, "BrowserLibrary.json")); }
        catch
        {
            _libraryLoadFailed = true;
            AddFavoriteButton.IsEnabled = FavoritesButton.IsEnabled = HistoryButton.IsEnabled = false;
            Status.Text = "收藏/历史文件无法读取，已保留原文件且禁用修改；网站登录数据不受影响。";
        }
    }
    private void RecordCompletedPage(string url, string title)
    {
        if (_library == null || _closing || !WebResourceRules.IsWebUrl(url)) return;
        try { _library.RecordVisit(url, title); }
        catch { Status.Text = "历史记录保存失败，本次访问未记录；请检查数据目录权限或磁盘空间。"; }
    }
    private void AddFavorite(object sender, RoutedEventArgs e)
    {
        var core = Browser.CoreWebView2;
        if (_library == null || core == null || !WebResourceRules.IsWebUrl(core.Source))
        { Status.Text = "请先打开要收藏的网页。"; return; }
        try { _library.AddFavorite(core.Source, core.DocumentTitle); Status.Text = "已收藏当前网页，可在收藏夹重命名或删除。"; }
        catch { Status.Text = "收藏保存失败，请检查网址、磁盘空间及数据目录权限。"; }
    }
    private void ShowFavorites(object sender, RoutedEventArgs e) => ShowLibrary(true);
    private void ShowHistory(object sender, RoutedEventArgs e) => ShowLibrary(false);
    private void ShowLibrary(bool favorites)
    {
        if (_library == null || _closing) return;
        var window = new BrowserLibraryWindow(_library, favorites) { Owner = this };
        window.ShowDialog();
        if (_closing || _closed || window.SelectedUrl == null) return;
        Address.Text = window.SelectedUrl;
        Navigate(this, new RoutedEventArgs());
    }
}
