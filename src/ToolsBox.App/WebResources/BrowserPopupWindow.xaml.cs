using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ToolsBox.Core.WebResources;

namespace ToolsBox.App.WebResources;

public partial class BrowserPopupWindow : Window
{
    private readonly WebView2 _browser;
    private readonly CoreWebView2 _core;
    public BrowserPopupWindow(WebView2 browser)
    {
        InitializeComponent();
        _browser = browser;
        _core = browser.CoreWebView2 ?? throw new InvalidOperationException("浏览器尚未就绪。");
        PopupBrowserHost.Child = browser;
        PopupAddress.Text = _core.Source;
        _core.SourceChanged += SourceChanged;
        Closed += (_, _) => ReleaseBrowser();
    }
    internal void ReleaseBrowser()
    {
        _core.SourceChanged -= SourceChanged;
        PopupBrowserHost.Child = null;
    }
    private void SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e) => PopupAddress.Text = _core.Source;
    private void Back(object sender, RoutedEventArgs e) { if (_core.CanGoBack) _browser.GoBack(); }
    private void Forward(object sender, RoutedEventArgs e) { if (_core.CanGoForward) _browser.GoForward(); }
    private void Reload(object sender, RoutedEventArgs e) => _browser.Reload();
    private void Navigate(object sender, RoutedEventArgs e)
    {
        var url = PopupAddress.Text.Trim(); if (!url.Contains("://")) url = "https://" + url;
        if (!WebResourceRules.IsWebUrl(url)) { PopupStatus.Text = "请输入不含账号密码的 HTTP(S) 地址。"; return; }
        _core.Navigate(url);
    }
    private void AddressKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Navigate(sender, e); }
    private void ReturnToTool(object sender, RoutedEventArgs e) => Close();
}
