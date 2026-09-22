using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;
using ToolsBox.App.WebResources;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] is not ("write" or "read")) return 2;
        var root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new WebResourceWindow(root) { ShowActivated = false, Left = -10000, Top = -10000 };
        window.Loaded += async (_, _) =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var browser = (WebView2)window.FindName("Browser");
                while (browser.CoreWebView2 == null) await Task.Delay(100, timeout.Token);
                var core = browser.CoreWebView2;
                var store = new BrowserLibraryStore(Path.Combine(root, "BrowserLibrary.json"));
                const string url = "https://login-probe.example/page";
                if (args[0] == "write")
                {
                    var cookie = core.CookieManager.CreateCookie("toolsbox_login_probe", "fixture", "login-probe.example", "/");
                    cookie.Expires = DateTime.UtcNow.AddDays(1); cookie.IsHttpOnly = true; cookie.IsSecure = true;
                    core.CookieManager.AddOrUpdateCookie(cookie);
                    store.AddFavorite(url, "持久收藏验证"); store.RecordVisit(url, "持久历史验证");
                }
                else
                {
                    if (store.Favorites.Single().Url != url || store.History.Single().Url != url) throw new Exception("跨进程收藏/历史恢复失败。");
                    var cookies = await core.CookieManager.GetCookiesAsync(url);
                    if (!cookies.Any(c => c.Name == "toolsbox_login_probe" && c.Value == "fixture")) throw new Exception("跨进程持久 Cookie 恢复失败。");
                    store.ClearHistory();
                    if (new BrowserLibraryStore(Path.Combine(root, "BrowserLibrary.json")).History.Count != 0) throw new Exception("历史未清空。");
                    if (!(await core.CookieManager.GetCookiesAsync(url)).Any(c => c.Name == "toolsbox_login_probe")) throw new Exception("清空历史错误地删除 Cookie。");
                }
                var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var environment = core.Environment;
                environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
                window.Close();
                await exited.Task.WaitAsync(timeout.Token);
                File.WriteAllText(Path.Combine(root, args[0] + "-result.txt"), "PASS " + args[0] + " persistent cookie + favorites/history; clean browser process exit");
                app.Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(root, args[0] + "-result.txt"), "FAIL " + ex);
                window.Close(); app.Shutdown(1);
            }
        };
        return app.Run(window);
    }
}
