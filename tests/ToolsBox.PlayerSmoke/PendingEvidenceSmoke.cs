using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Web.WebView2.Core;
using ToolsBox.App.WebResources;
using ToolsBox.Core.WebResources;

internal static class PendingEvidenceSmoke
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Origin = "https://www.douyin.com";

    public static async Task Run(WebResourceWindow window, CoreWebView2 core, byte[] sample, CancellationToken ct)
    {
        var streams = new List<Stream>();
        var held = new Dictionary<string, (CoreWebView2WebResourceRequestedEventArgs Args, CoreWebView2Deferral Deferral)>();
        var responded = new HashSet<string>(StringComparer.Ordinal);
        string[] spaPaths = ["/aweme/v1/web/tab/feed/?case=spa", "/pending-spa.mp4", "/pending-spa.png"];
        string[] documentPaths = ["/aweme/v1/web/aweme/detail/?case=document", "/pending-document.mp4"];
        var delayed = spaPaths.Concat(documentPaths).ToHashSet(StringComparer.Ordinal);
        var catalog = (Dictionary<string, DouyinMediaItem>)Field(window, "_douyinMedia");
        var observed = (Dictionary<string, WebResourceKind>)Field(window, "_playerObservedMedia");
        var rows = (ObservableCollection<WebResourceRow>)Field(window, "_resources");

        void Respond(CoreWebView2WebResourceRequestedEventArgs args)
        {
            string path = new Uri(args.Request.Uri).PathAndQuery;
            string mime;
            byte[] body;
            if (path.StartsWith("/aweme/", StringComparison.Ordinal))
            {
                string id = path.Contains("case=spa", StringComparison.Ordinal) ? "456" : "789";
                mime = "application/json";
                body = Encoding.UTF8.GetBytes("{\"aweme_list\":[{\"aweme_id\":\"" + id +
                    "\",\"desc\":\"Pending response fixture\",\"video\":{\"duration\":4000,\"play_addr\":{\"url_list\":[\"" +
                    Origin + "/pending-" + (id == "456" ? "spa" : "document") + ".mp4\"]}}}]}");
            }
            else if (path.EndsWith(".mp4", StringComparison.Ordinal)) { mime = "video/mp4"; body = sample; }
            else if (path.EndsWith(".png", StringComparison.Ordinal))
            {
                mime = "image/png";
                body = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aD1sAAAAASUVORK5CYII=");
            }
            else { mime = "text/html; charset=utf-8"; body = Encoding.UTF8.GetBytes("<!doctype html><title>Pending evidence fixture</title>"); }
            var stream = new MemoryStream(body, writable: false); streams.Add(stream);
            args.Response = core.Environment.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: " + mime + "\r\nCache-Control: no-store");
        }

        void Requested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            if (new Uri(args.Request.Uri).GetLeftPart(UriPartial.Authority) != Origin) return;
            string path = new Uri(args.Request.Uri).PathAndQuery;
            if (delayed.Contains(path)) held.Add(path, (args, args.GetDeferral()));
            else Respond(args);
        }

        void Responded(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
        {
            if (new Uri(args.Request.Uri).GetLeftPart(UriPartial.Authority) == Origin)
                responded.Add(new Uri(args.Request.Uri).PathAndQuery);
        }

        void Release(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                var request = held[path]; held.Remove(path);
                try { Respond(request.Args); }
                finally { request.Deferral.Complete(); }
            }
        }

        async Task BeginRequests(string[] paths)
        {
            string urls = System.Text.Json.JsonSerializer.Serialize(paths);
            await core.ExecuteScriptAsync("window.__pendingEvidenceDone=false; Promise.all(" + urls +
                ".map(u=>fetch(u).then(r=>r.arrayBuffer()))).then(()=>window.__pendingEvidenceDone=true); true;");
            await Until(() => Task.FromResult(paths.All(held.ContainsKey)), ct);
        }

        core.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += Requested;
        core.WebResourceResponseReceived += Responded;
        try
        {
            await Navigate(core, Origin + "/pending-evidence-fixture", ct);
            await BeginRequests(spaPaths);
            await core.ExecuteScriptAsync("history.pushState({},'', '/pending-evidence-fixture?video=456')");
            await Until(() => Task.FromResult((string)Field(window, "_page") == Origin + "/pending-evidence-fixture?video=456"), ct);
            Release(spaPaths);
            await Until(async () => await core.ExecuteScriptAsync("window.__pendingEvidenceDone===true") == "true" &&
                spaPaths.All(responded.Contains) && (int)Field(window, "_metadataReads") == 0, ct);

            Require(catalog.ContainsKey("456"), "Same-document pending feed metadata was lost after history.pushState.");
            Require(observed.ContainsKey(Origin + "/pending-spa.mp4"), "Same-document pending media evidence was lost after history.pushState.");
            Require(!rows.Any(row => spaPaths.Any(path => row.Url == Origin + path)), "A previous-page raw resource row leaked into the new SPA address.");

            await BeginRequests(documentPaths);
            await Navigate(core, Origin + "/pending-evidence-destination", ct);
            Release(documentPaths);
            // Chromium may cancel fetches when the old document is destroyed. A later
            // same-origin response provides a completion point without reading any body twice.
            await core.ExecuteScriptAsync("window.__pendingEvidenceBarrier=false; fetch('/evidence-barrier').then(r=>r.text()).then(()=>window.__pendingEvidenceBarrier=true); true;");
            await Until(async () => await core.ExecuteScriptAsync("window.__pendingEvidenceBarrier===true") == "true" &&
                (int)Field(window, "_metadataReads") == 0, ct);
            Require(!catalog.ContainsKey("456") && !catalog.ContainsKey("789"), "Metadata survived or repopulated after real document navigation.");
            Require(!observed.ContainsKey(Origin + "/pending-document.mp4"), "Old-document pending media evidence was accepted after navigation.");
            Require(!rows.Any(row => documentPaths.Any(path => row.Url == Origin + path)), "Old-document pending response populated the destination list.");
        }
        finally
        {
            core.WebResourceRequested -= Requested;
            core.WebResourceResponseReceived -= Responded;
            foreach (var request in held.Values)
            {
                try { Respond(request.Args); }
                finally { request.Deferral.Complete(); }
            }
            foreach (var stream in streams) stream.Dispose();
            core.RemoveWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
        }
    }

    private static object Field(WebResourceWindow window, string name) => window.GetType().GetField(name, Private)!.GetValue(window)!;

    private static async Task Navigate(CoreWebView2 core, string url, CancellationToken ct)
    {
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args) => loaded.TrySetResult(args.IsSuccess);
        core.NavigationCompleted += Completed;
        try
        {
            core.Navigate(url);
            Require(await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20), ct), "Pending evidence fixture navigation failed.");
        }
        finally { core.NavigationCompleted -= Completed; }
    }

    private static async Task Until(Func<Task<bool>> condition, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (await condition()) return;
            await Task.Delay(100, ct);
        }
        throw new TimeoutException("Pending evidence fixture did not reach the expected state.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
