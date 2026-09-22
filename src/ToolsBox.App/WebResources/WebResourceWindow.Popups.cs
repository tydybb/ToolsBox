using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using ToolsBox.Core.WebResources;

namespace ToolsBox.App.WebResources;

public partial class WebResourceWindow
{
    private void BrowserNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (_closing || _closed) return;

        string target = args.Uri;
        if (!NewWindowNavigationPolicy.CanNavigate(target, args.IsUserInitiated))
        {
            Status.Text = args.IsUserInitiated
                ? "已阻止不支持的链接，仅允许打开不含账号密码的 HTTP(S) 网页。"
                : "已阻止网页自动弹出窗口，请点击要访问的链接。";
            return;
        }

        var core = Browser.CoreWebView2;
        if (core == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        try
        {
            string source = core.Source;
            long generation = _playerDocumentGeneration;
            // Let the new-window event finish before navigating the existing browser.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_closing || _closed || generation != _playerDocumentGeneration ||
                    !ReferenceEquals(core, Browser.CoreWebView2)) return;
                try
                {
                    if (!string.Equals(core.Source, source, StringComparison.Ordinal)) return;
                    core.Navigate(target);
                    Status.Text = "正在当前工具浏览器打开链接。";
                }
                catch (Exception error) when (error is InvalidOperationException or COMException)
                {
                    if (!_closing && !_closed) Status.Text = "链接打开失败，请稍后重试。";
                }
            }));
        }
        catch (Exception error) when (error is InvalidOperationException or COMException)
        {
            if (!_closing && !_closed) Status.Text = "链接打开失败，请稍后重试。";
        }
    }
}
