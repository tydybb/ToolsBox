using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using ToolsBox.App.WebResources;
using ToolsBox.Core.WebResources;
using ToolsBox.MediaDownloads;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 2) return 2;
        var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        var lines = new List<string>();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new WebResourceWindow(root) { ShowActivated = false, Left = -10000, Top = -10000 };
        window.Loaded += async (_, _) =>
        {
            string stage = "initialize";
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(100));
                var browser = (WebView2)window.FindName("Browser");
                while (browser.CoreWebView2 == null) await Task.Delay(100, deadline.Token);
                var core = browser.CoreWebView2;
                ((CheckBox)window.FindName("CaptureEnabled")).IsChecked = false;
                const string url = "https://www.douyin.com/";
                stage = "navigate-homepage";
                var navigated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                core.NavigationCompleted += (_, e) => navigated.TrySetResult(e.IsSuccess);
                core.Navigate(url);
                lines.Add("navigation-success=" + await navigated.Task.WaitAsync(deadline.Token));
                // Give the site's ordinary client-side redirects time to finish; do not interact with challenges.
                await Task.Delay(TimeSpan.FromSeconds(20), deadline.Token);
                lines.Add("still-on-homepage=" + (core.Source == url));
                var currentUri = new Uri(core.Source);
                lines.Add("source-is-douyin=" + (currentUri.Host is "www.douyin.com" or "douyin.com"));
                lines.Add("source-path-kind=" + (currentUri.AbsolutePath switch { "/" => "root", "/discover" => "discover", "/recommend" => "recommend", _ => "other" }));
                // Only a short alphabetic public route from this fresh anonymous homepage visit; never query data or IDs.
                if (System.Text.RegularExpressions.Regex.IsMatch(currentUri.AbsolutePath, "^/[a-zA-Z/]{0,40}$"))
                    lines.Add("public-route=" + currentUri.AbsolutePath);
                lines.Add("challenge-page=" + core.DocumentTitle.Contains("Please wait", StringComparison.OrdinalIgnoreCase));
                stage = "read-native-cookies";
                var native = await core.CookieManager.GetCookiesAsync(url);
                lines.Add("cookie-count=" + native.Count);
                lines.Add("empty-name-count=" + native.Count(c => string.IsNullOrEmpty(c.Name)));
                lines.Add("control-character-count=" + native.Count(c => new[] { c.Name, c.Value, c.Domain, c.Path }.Any(s => s.Any(char.IsControl))));
                lines.Add("invalid-domain-count=" + native.Count(c => Uri.CheckHostName(c.Domain.TrimStart('.')) == UriHostNameType.Unknown));
                lines.Add("invalid-path-count=" + native.Count(c => !c.Path.StartsWith('/')));
                stage = "homepage-button-guidance";
                typeof(WebResourceWindow).GetMethod("InspectPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
                lines.Add("homepage-button-guidance=" + ((TextBlock)window.FindName("Status")).Text.Contains("单条视频"));
                stage = "app-cookie-conversion";
                var method = typeof(WebResourceWindow).GetMethod("CookiesFor", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var cookies = await (Task<IReadOnlyList<MediaCookie>>)method.Invoke(window, [new WebResourceRow(url, url, WebResourceKind.Video, null)])!;
                lines.Add("app-cookie-conversion=PASS");
                stage = "temporary-cookie-file";
                var cookieType = typeof(MediaDownloadService).Assembly.GetType("ToolsBox.MediaDownloads.CookieFile")!;
                using (var cookieFile = (IDisposable?)cookieType.GetMethod("Create")!.Invoke(null, [cookies])) lines.Add("temporary-cookie-file=PASS");
                stage = "inspect-homepage";
                using var parseDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var service = new MediaDownloadService(new ComponentManager(Path.GetFullPath(args[1])));
                await service.InspectVideoAsync(new Uri(url), parseDeadline.Token, cookies, url, core.Settings.UserAgent);
                lines.Add("inspect-homepage=PASS");
            }
            catch (Exception error)
            {
                while (error is TargetInvocationException { InnerException: not null } invocation) error = invocation.InnerException!;
                lines.Add("failed-stage=" + stage);
                lines.Add("exception-type=" + error.GetType().FullName);
                if (error is MediaDownloadException media) lines.Add("classified-media-error=" + media.Message);
                // Never persist raw messages, argument values, stderr or cookie contents.
                foreach (var frame in new StackTrace(error).GetFrames().Take(8))
                { var method = frame.GetMethod(); lines.Add("method=" + method?.DeclaringType?.FullName + "." + method?.Name); }
            }
            finally
            {
                File.WriteAllLines(Path.Combine(root, "diagnostic-result.txt"), lines);
                window.Close(); app.Shutdown();
            }
        };
        return app.Run(window);
    }
}
