using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;
using ToolsBox.App.WebResources;
using ToolsBox.Core.WebResources;
using ToolsBox.MediaDownloads;

internal static partial class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length is < 2 or > 3) return 2;
        string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        var lines = new List<string>(); var routes = new HashSet<string>();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new WebResourceWindow(root) { ShowActivated = false, Left = -10000, Top = -10000, Height = 1500 };
        window.Loaded += async (_, _) =>
        {
            string stage = "initialize";
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
                var browser = (WebView2)window.FindName("Browser");
                while (browser.CoreWebView2 == null || window.GetType().GetField("_playerTimer", Private)!.GetValue(window) == null)
                    await Task.Delay(100, deadline.Token);
                var core = browser.CoreWebView2;
                if (args.Length == 3 && args[2] == "hover")
                {
                    stage = "douyin-hover";
                    await ProbeHover(window, root, lines, deadline.Token);
                    return;
                }
                if (args.Length == 3 && args[2] is "feed" or "home" or "recommend" or "feed-download" or "feed-diagnose")
                {
                    stage = "continuous-douyin";
                    await ProbeFeed(window, root, lines, args[2], args[1], deadline.Token);
                    return;
                }
                if (args.Length == 3 && args[2] == "bilibili")
                {
                    stage = "bilibili-popup"; bool clickedPopup = false;
                    core.NewWindowRequested += (_, e) => { clickedPopup |= e.IsUserInitiated && e.Uri.StartsWith("https://www.bilibili.com/video/"); };
                    core.Navigate("https://www.bilibili.com/");
                    for (int i = 0; i < 40; i++)
                    {
                        await Task.Delay(1000, deadline.Token);
                        string hit = await core.ExecuteScriptAsync("(() => {for(const a of document.querySelectorAll('a[href]')) { if(!a.href.startsWith('https://www.bilibili.com/video/')) continue; const r=a.getBoundingClientRect(); if(r.width>30&&r.height>30&&r.top>=0&&r.bottom<innerHeight&&r.left>=0&&r.right<innerWidth) return {x:r.left+r.width/2,y:r.top+r.height/2}; } return null;})()");
                        using var point = System.Text.Json.JsonDocument.Parse(hit);
                        if (point.RootElement.ValueKind == System.Text.Json.JsonValueKind.Null) continue;
                        double x = point.RootElement.GetProperty("x").GetDouble(), y = point.RootElement.GetProperty("y").GetDouble();
                        foreach (string type in new[] { "mousePressed", "mouseReleased" }) await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", System.Text.Json.JsonSerializer.Serialize(new { type, x, y, button = "left", clickCount = 1 }));
                        for (int j = 0; j < 20 && !core.Source.StartsWith("https://www.bilibili.com/video/"); j++) await Task.Delay(500, deadline.Token);
                        break;
                    }
                    lines.Add("bilibili-user-popup=" + clickedPopup);
                    lines.Add("bilibili-current-view-video=" + core.Source.StartsWith("https://www.bilibili.com/video/"));
                    lines.Add("bilibili-same-core=" + ReferenceEquals(core, browser.CoreWebView2));
                    lines.Add("bilibili-can-go-back=" + core.CanGoBack);
                    return;
                }
                core.WebResourceResponseReceived += (_, e) =>
                {
                    var u = new Uri(e.Request.Uri);
                    if (u.Host is "www.douyin.com" or "douyin.com" && u.AbsolutePath.StartsWith("/aweme/v1/web/")) routes.Add(u.AbsolutePath);
                    // GetContentAsync streams may share a position. Only the production observer reads bodies.
                };
                stage = "navigate-user-video";
                core.Navigate("https://www.douyin.com/jingxuan?modal_id=7680095187900697907");
                PlayerDocument? doc = null; PlayerSnapshot? player = null; BoundPlayerMedia? bound = null;
                for (int i = 0; i < 60; i++)
                {
                    await Task.Delay(1000, deadline.Token);
                    // The prior isolated screenshot shows the site's ordinary dismissible login prompt.
                    // Close its visible X; do not enter credentials or bypass a challenge.
                    if (i is 12 or 30 or 45 && await core.ExecuteScriptAsync("document.body.innerText.includes('登录后免费畅享高清视频')") == "true")
                    {
                        foreach (string type in new[] { "mousePressed", "mouseReleased" })
                            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", System.Text.Json.JsonSerializer.Serialize(new { type, x = 962, y = 167, button = "left", clickCount = 1 }));
                    }
                    string tutorial = await core.ExecuteScriptAsync("(() => {const e=Array.from(document.querySelectorAll('button,span,div')).find(e=>e.children.length<=1&&e.textContent.trim()==='我知道了');if(!e)return null;const r=e.getBoundingClientRect();return r.width>0&&r.height>0?{x:r.left+r.width/2,y:r.top+r.height/2}:null;})()");
                    using (var point = System.Text.Json.JsonDocument.Parse(tutorial))
                        if (point.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                            foreach (string type in new[] { "mousePressed", "mouseReleased" })
                                await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", System.Text.Json.JsonSerializer.Serialize(new { type, x = point.RootElement.GetProperty("x").GetDouble(), y = point.RootElement.GetProperty("y").GetDouble(), button = "left", clickCount = 1 }));
                    doc = await (Task<PlayerDocument?>)window.GetType().GetMethod("ReadPlayerDocument", Private)!.Invoke(window, [0])!;
                    if (doc == null) continue;
                    var observed = (Dictionary<string, WebResourceKind>)window.GetType().GetField("_playerObservedMedia", Private)!.GetValue(window)!;
                    var catalog = (Dictionary<string, DouyinMediaItem>)window.GetType().GetField("_douyinMedia", Private)!.GetValue(window)!;
                    player = doc.Players.FirstOrDefault(p => p.Visible && p.SiteId == "7680095187900697907");
                    if (player != null) bound = PlayerMediaBinding.Resolve(doc, player, observed, catalog);
                    if (bound != null && (args.Length != 3 || args[2] != "capture-only" || catalog.Count > 0)) break;
                }
                lines.Add("document-observed=" + (doc != null));
                lines.Add("players=" + (doc?.Players.Length ?? 0));
                lines.Add("visible-players=" + (doc?.Players.Count(p => p.Visible) ?? 0));
                lines.Add("target-player=" + (player != null));
                lines.Add("target-duration=" + player?.Duration);
                lines.Add("bound=" + (bound != null));
                if (bound != null)
                {
                    await (Task)window.GetType().GetMethod("PollPlayers", Private)!.Invoke(window, null)!;
                    var resources = (System.Collections.ObjectModel.ObservableCollection<WebResourceRow>)window.GetType().GetField("_resources", Private)!.GetValue(window)!;
                    int count = resources.Count(r => r.CapturedVideo && r.PageUrl == doc!.Url && bound.Choices.Any(c => c.Url == r.Url));
                    lines.Add("auto-list-bound-video-count=" + count);
                    if (count == 0) throw new InvalidOperationException("Bound player was not automatically published.");
                }
                lines.Add("media-records=" + ((Dictionary<string, DouyinMediaItem>)window.GetType().GetField("_douyinMedia", Private)!.GetValue(window)!).Count);
                lines.Add("metadata-diagnostic=" + window.GetType().GetField("_lastPlayerMetadataResult", Private)!.GetValue(window));
                lines.Add("metadata-routes=" + string.Join(',', routes.Order()));
                lines.Add("player-states=" + string.Join(';', doc?.Players.Select(p => $"visible={p.Visible},siteId={p.SiteId},source={(p.Source.StartsWith("blob:") ? "blob" : p.Source.Length == 0 ? "empty" : "http")},duration={p.Duration}") ?? []));
                lines.Add("visibility-details=" + await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('video')).map(v=>{const r=v.getBoundingClientRect(),h=document.elementFromPoint(Math.max(0,r.left)+Math.min(r.width,innerWidth)/2,Math.max(0,r.top)+Math.min(r.height,innerHeight)/2);return {rect:{x:r.x,y:r.y,w:r.width,h:r.height},style:getComputedStyle(v).visibility,opacity:getComputedStyle(v).opacity,parent:v.parentElement?.className,active:!!v.closest('[data-e2e=feed-active-video]'),hit:h?.className};})"));
                await using (var preview = File.Create(Path.Combine(root, "page.png"))) await core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, preview);
                if (bound == null || args.Length == 3 && args[2] == "capture-only") return;
                lines.Add("qualities=" + bound.Choices.Count);
                stage = "read-confirmed-media-cookies";
                // This diagnostic is explicitly run to verify the user's concrete video; it never exports credentials.
                var choice = bound.Choices.Last();
                var row = new WebResourceRow(choice.Url, doc!.Url, choice.Kind, null);
                var cookies = await (Task<IReadOnlyList<MediaCookie>>)window.GetType().GetMethod("CookiesFor", Private)!.Invoke(window, [row])!;
                stage = "download-bound-media";
                var service = new MediaDownloadService(new ComponentManager(Path.GetFullPath(args[1])));
                string output = await service.DownloadCapturedVideoAsync(new Uri(choice.Url), Path.Combine(root, "downloads"), "douyin-user-video-verification", bound.Duration,
                    ct: deadline.Token, cookies: cookies, referer: doc.Url, userAgent: core.Settings.UserAgent);
                lines.Add("validated-mp4=PASS"); lines.Add("output-bytes=" + new FileInfo(output).Length);
            }
            catch (Exception error)
            {
                while (error is TargetInvocationException { InnerException: not null } target) error = target.InnerException!;
                lines.Add("failed-stage=" + stage); lines.Add("exception=" + error.GetType().Name);
                if (error is MediaDownloadException media) lines.Add("classified-error=" + media.Message);
            }
            finally
            {
                File.WriteAllLines(Path.Combine(root, "player-probe-result.txt"), lines);
                window.Close(); app.Shutdown();
            }
        };
        return app.Run(window);
    }
}
