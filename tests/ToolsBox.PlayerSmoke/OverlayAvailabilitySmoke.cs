using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using ToolsBox.App.WebResources;

internal static class OverlayAvailabilitySmoke
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Origin = "https://overlay-availability.example.test";
    private const string Button = "document.querySelector('[data-toolsbox-player]')?.shadowRoot.querySelector('button')";

    public static async Task Run(WebResourceWindow window, CoreWebView2 core, byte[] sampleBytes, CancellationToken ct)
    {
        bool evidence = false;
        var streams = new List<Stream>();
        var tasks = (ObservableCollection<WebDownloadRow>)Field(window, "_tasks");
        int originalTasks = tasks.Count;
        var timer = (DispatcherTimer)Field(window, "_playerTimer");
        core.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
        void Requested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            string path = new Uri(args.Request.Uri).AbsolutePath;
            bool media = path.EndsWith(".bin", StringComparison.Ordinal);
            string body = path == "/frame" ?
                "<!doctype html><style>body{margin:0}video{display:block}</style><video width=240 height=135 muted preload=auto src='/frame-media.bin'></video>" :
                "<!doctype html><title>Overlay availability</title><style>body{margin:0}video,iframe{display:block}</style><video width=240 height=135 muted preload=auto src='/top-media.bin'></video><iframe width=280 height=180 src='/frame'></iframe>";
            byte[] bytes = media ? sampleBytes : Encoding.UTF8.GetBytes(body);
            var stream = new MemoryStream(bytes, writable: false); streams.Add(stream);
            // A playable source alone is not native evidence. A later response supplies its media type.
            args.Response = core.Environment.CreateWebResourceResponse(stream, 200, "OK",
                "Content-Type: " + (media ? evidence ? "video/mp4" : "application/octet-stream" : "text/html; charset=utf-8") + "\r\nCache-Control: no-store");
        }
        core.WebResourceRequested += Requested;
        try
        {
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args) => completed.TrySetResult(args.IsSuccess);
            core.NavigationCompleted += Completed;
            try { core.Navigate(Origin + "/page"); Require(await completed.Task.WaitAsync(TimeSpan.FromSeconds(20), ct), "Overlay fixture did not load."); }
            finally { core.NavigationCompleted -= Completed; }
            await Until(core, "document.querySelector('video').duration>0 && document.querySelector('iframe').contentDocument.querySelector('video').duration>0 && !!(" + Button + ")", ct);
            await Poll(window);
            Require(await Evaluate<bool>(core, "(()=>{const b=" + Button + ";return b.disabled && /待识别|请先播放/.test(b.textContent)})()"),
                "Unbound visible player advertised an enabled Download button.");
            Require(await Evaluate<bool>(core, "document.querySelector('iframe').contentDocument.querySelector('[data-toolsbox-player]').shadowRoot.querySelector('button').disabled"),
                "Unbound iframe player advertised an enabled Download button.");
            await Click(core, Button, ct);
            await Task.Delay(200, ct);
            Require(!Application.Current.Windows.Cast<Window>().Any(w => w.GetType().Name == "PlayerDownloadDialog") && tasks.Count == originalTasks,
                "Disabled overlay opened a confirmation or started a download.");

            evidence = true;
            await core.ExecuteScriptAsync("fetch('/top-media.bin',{cache:'reload'}).then(r=>r.arrayBuffer());document.querySelector('iframe').contentWindow.fetch('/frame-media.bin',{cache:'reload'}).then(r=>r.arrayBuffer());void 0");
            await Until(core, "!!(" + Button + ") && !(" + Button + ").disabled && !document.querySelector('iframe').contentDocument.querySelector('[data-toolsbox-player]').shadowRoot.querySelector('button').disabled", ct);
            Require(await Evaluate<bool>(core, "document.querySelector('video').paused && document.querySelector('iframe').contentDocument.querySelector('video').paused"),
                "Readiness detection unexpectedly started playback.");
            await Click(core, Button, ct);
            await Until(() => Application.Current.Windows.Cast<Window>().Any(w => w.GetType().Name == "PlayerDownloadDialog"), ct);
            var dialog = Application.Current.Windows.Cast<Window>().Single(w => w.GetType().Name == "PlayerDownloadDialog");
            Require(tasks.Count == originalTasks, "Enabled overlay bypassed native confirmation.");
            dialog.DialogResult = false;
            await Until(() => !(bool)Field(window, "_confirmingPlayer"), ct);

            timer.Stop();
            await Until(() => !(bool)Field(window, "_pollingPlayers"), ct);
            Require(await Evaluate<bool>(core, "(()=>{document.querySelector('video').dispatchEvent(new Event('emptied'));return (" + Button + ").disabled})()"),
                "Emptied player retained readiness before native polling.");
            await Poll(window);
            Require(await Evaluate<bool>(core, "!(" + Button + ").disabled"), "Valid paused player did not regain native readiness.");
            Require(await Evaluate<bool>(core, "(()=>{document.querySelector('video').dispatchEvent(new Event('encrypted'));return (" + Button + ").disabled})()"),
                "Encrypted player retained an enabled button before native polling.");
            await core.ExecuteScriptAsync("document.querySelector('video').dispatchEvent(new Event('emptied'));void 0");
            await Poll(window);
            evidence = false;
            await core.ExecuteScriptAsync("document.querySelector('video').src='/changed-media.bin';document.querySelector('video').load();void 0");
            await Until(core, "document.querySelector('video').currentSrc.endsWith('/changed-media.bin') && document.querySelector('video').duration>0", ct);
            Require(await Evaluate<bool>(core, "(window.__toolsboxPlayers(),(" + Button + ").disabled)"), "Source replacement inherited the old ready button.");
            await Poll(window);
            Require(await Evaluate<bool>(core, "(" + Button + ").disabled"), "Unknown replacement source became ready after native polling.");
            Require(tasks.Count == originalTasks, "Availability checks started a download task.");
        }
        finally
        {
            timer.Start();
            core.WebResourceRequested -= Requested;
            core.RemoveWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
            foreach (var stream in streams) stream.Dispose();
        }
    }

    private static object Field(WebResourceWindow window, string name) => window.GetType().GetField(name, Private)!.GetValue(window)!;
    private static Task Poll(WebResourceWindow window) => (Task)window.GetType().GetMethod("PollPlayers", Private)!.Invoke(window, null)!;
    private static async Task<T> Evaluate<T>(CoreWebView2 core, string script) => JsonSerializer.Deserialize<T>(await core.ExecuteScriptAsync(script))!;
    private static async Task Until(CoreWebView2 core, string script, CancellationToken ct)
    {
        for (int i = 0; i < 200; i++) { ct.ThrowIfCancellationRequested(); if (await Evaluate<bool>(core, script)) return; await Task.Delay(100, ct); }
        throw new TimeoutException("Overlay availability state did not appear.");
    }
    private static async Task Until(Func<bool> condition, CancellationToken ct)
    {
        for (int i = 0; i < 200; i++) { ct.ThrowIfCancellationRequested(); if (condition()) return; await Task.Delay(100, ct); }
        throw new TimeoutException("Native overlay confirmation state did not appear.");
    }
    private static async Task Click(CoreWebView2 core, string expression, CancellationToken ct)
    {
        var point = await Evaluate<double[]>(core, "(()=>{const r=(" + expression + ").getBoundingClientRect();return [r.left+r.width/2,r.top+r.height/2]})()");
        ct.ThrowIfCancellationRequested();
        foreach (string type in new[] { "mouseMoved", "mousePressed", "mouseReleased" })
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new
            { type, x = point[0], y = point[1], button = type == "mouseMoved" ? "none" : "left", clickCount = type == "mouseMoved" ? 0 : 1 }));
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
