using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using ToolsBox.App.WebResources;

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        var app = new Application();
        var stateDirectory = Path.Combine(AppContext.BaseDirectory, "smoke-state");
        var window = new WebResourceWindow(stateDirectory) { ShowActivated = false, Left = -10000, Top = -10000 };
        window.Loaded += async (_, _) =>
        {
            try
            {
                var browser = (WebView2)window.FindName("Browser");
                if (window.FindName("ExportThunderButton") != null || window.FindName("CancelExportButton") != null)
                    throw new Exception("已移除的迅雷导出入口仍然存在。");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                while (browser.CoreWebView2 == null) await Task.Delay(100, timeout.Token);
                var tcp = new TcpListener(IPAddress.Loopback, 0); tcp.Start(); int port = ((IPEndPoint)tcp.LocalEndpoint).Port; tcp.Stop();
                using var listener = new HttpListener(); listener.Prefixes.Add($"http://localhost:{port}/"); listener.Start();
                var server = Task.Run(async () =>
                {
                    while (!timeout.IsCancellationRequested)
                    {
                        HttpListenerContext context;
                        try { context = await listener.GetContextAsync().WaitAsync(timeout.Token); } catch { break; }
                        bool image = context.Request.Url!.AbsolutePath == "/image.png";
                        if (image && context.Request.Cookies["session"]?.Value != "fixture")
                        { context.Response.StatusCode = 403; context.Response.Close(); continue; }
                        if (!image) context.Response.Headers.Add("Set-Cookie", "session=fixture; Path=/; HttpOnly");
                        byte[] content = image ? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a2ioAAAAASUVORK5CYII=") : System.Text.Encoding.UTF8.GetBytes("<html><body>Local resource test<img src='/image.png'></body></html>");
                        context.Response.ContentType = image ? "image/png" : "text/html";
                        context.Response.ContentLength64 = content.Length;
                        await context.Response.OutputStream.WriteAsync(content, timeout.Token); context.Response.Close();
                    }
                });
                browser.CoreWebView2.Navigate($"http://localhost:{port}/");
                var grid = (DataGrid)window.FindName("ResourcesGrid");
                while (!grid.Items.OfType<WebResourceRow>().Any(r => r.Url.EndsWith("/image.png"))) await Task.Delay(100, timeout.Token);
                var row = grid.Items.OfType<WebResourceRow>().First(r => r.Url.EndsWith("/image.png"));
                while (!new BrowserLibraryStore(Path.Combine(stateDirectory, "BrowserLibrary.json")).History.Any(h => h.Url == $"http://localhost:{port}/")) await Task.Delay(100, timeout.Token);
                row.IsSelected = true;
                var resources = (System.Collections.ObjectModel.ObservableCollection<WebResourceRow>)grid.ItemsSource;
                resources.Add(new WebResourceRow($"http://localhost:{port}/hidden.mp4", $"http://localhost:{port}/", ToolsBox.Core.WebResources.WebResourceKind.Video, null) { IsSelected = true });
                ((ComboBox)window.FindName("KindFilter")).SelectedIndex = 1;
                var output = Path.Combine(AppContext.BaseDirectory, "smoke-downloads"); Directory.CreateDirectory(output);
                ((TextBox)window.FindName("OutputFolder")).Text = output;
                window.GetType().GetMethod("DownloadSelected", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
                var jobs = (DataGrid)window.FindName("TasksGrid");
                if (jobs.Items.Count != 1) throw new Exception("隐藏勾选资源不应进入本次下载。");
                while (!jobs.Items.OfType<WebDownloadRow>().Any() || jobs.Items.OfType<WebDownloadRow>().Any(j => j.IsActive)) await Task.Delay(100, timeout.Token);
                if (!jobs.Items.OfType<WebDownloadRow>().All(j => j.Succeeded && File.Exists(j.OutputPath))) throw new Exception("内置登录态图片下载失败。");
                if (new WindowsWebRuntimeBackend().DetectVersion() == null) throw new Exception("运行时检测未识别现有 WebView2。");
                var originalCore = browser.CoreWebView2;
                window.GetType().GetMethod("PopOutBrowser", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
                var popup = (BrowserPopupWindow?)window.GetType().GetField("_browserPopup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window);
                if (popup == null || ((Border)popup.FindName("PopupBrowserHost")).Child != browser) throw new Exception("浏览器弹出失败。");
                var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                originalCore.NavigationCompleted += (_, e) => { if (e.IsSuccess) navigated.TrySetResult(); };
                originalCore.Reload(); await navigated.Task.WaitAsync(timeout.Token);
                popup.Close();
                if (((Border)window.FindName("BrowserHost")).Child != browser || browser.CoreWebView2 != originalCore) throw new Exception("浏览器放回时丢失原实例。");
                // pushState changes the source without a top-level navigation. Parsing must use this live source.
                int navigationStarts = 0;
                originalCore.NavigationStarting += (_, _) => navigationStarts++;
                await originalCore.ExecuteScriptAsync("history.pushState({}, '', '/spa-selected?video=123')");
                string spaSource = $"http://localhost:{port}/spa-selected?video=123";
                while (((TextBox)window.FindName("Address")).Text != spaSource) await Task.Delay(50, timeout.Token);
                if (navigationStarts != 0) throw new Exception("SPA 测试意外发生了整页导航。");
                window.GetType().GetField("_page", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, $"http://localhost:{port}/stale-page");
                window.GetType().GetMethod("InspectPage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
                while (!resources.Any(r => r.Url == spaSource)) await Task.Delay(50, timeout.Token);
                if (resources.Single(r => r.Url == spaSource).PageUrl != spaSource) throw new Exception("解析没有保留实际来源页面。");
                var operations = (HashSet<Task>)window.GetType().GetField("_operations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                while (operations.Count > 0) await Task.Delay(50, timeout.Token);
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "browser-smoke-result.txt"), "PASS ordinary browser + navigation history + checkbox authenticated image download + hidden selection excluded + no Thunder export entry + popout/reload/return same browser + SPA current-source parsing; runtime=" + browser.CoreWebView2.Environment.BrowserVersionString);
                timeout.Cancel(); listener.Stop(); await server;
                window.Close(); app.Shutdown(0);
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "browser-smoke-result.txt"), "FAIL " + error.GetType().Name + ": " + error.Message);
                window.Close(); app.Shutdown(1);
            }
        };
        return app.Run(window);
    }
}
