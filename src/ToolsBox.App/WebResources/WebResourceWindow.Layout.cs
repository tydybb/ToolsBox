using System.Windows;

namespace ToolsBox.App.WebResources;

public partial class WebResourceWindow
{
    private BrowserPopupWindow? _browserPopup;
    private void PopOutBrowser(object sender, RoutedEventArgs e)
    {
        if (_browserPopup != null)
        { if (_browserPopup.WindowState == WindowState.Minimized) _browserPopup.WindowState = WindowState.Normal; _browserPopup.Activate(); return; }
        if (Browser.CoreWebView2 == null) { Status.Text = "浏览器尚未就绪，请先检查浏览器环境。"; return; }
        BrowserHost.Child = null;
        try
        {
            var popup = new BrowserPopupWindow(Browser) { Owner = this };
            _browserPopup = popup;
            popup.Closed += (_, _) =>
            {
                _browserPopup = null;
                if (!_closed && !_closing) BrowserHost.Child = Browser;
                PopOutHint.Visibility = Visibility.Collapsed;
            };
            popup.Show();
            PopOutHint.Visibility = Visibility.Visible;
        }
        catch
        {
            _browserPopup?.ReleaseBrowser(); _browserPopup?.Close(); _browserPopup = null;
            BrowserHost.Child = Browser;
            Status.Text = "独立浏览窗口打开失败，网页已放回工具窗口。";
        }
    }
}
