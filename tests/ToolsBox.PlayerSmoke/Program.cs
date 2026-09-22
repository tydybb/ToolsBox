using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ToolsBox.App.WebResources;
using ToolsBox.Core.WebResources;
using ToolsBox.MediaDownloads;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> Checks = [];
    private static string _stage = "startup";
    private static string _state = "";
    private static double? _duration;
    private static bool _hasAudio;
    private static string _runtime = "";
    private static bool? _metadataWithoutLength;
    private static int? _metadataCount;
    private static string? _metadataStatus;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length is not (2 or 3) || args.Length == 3 && args[2] is not ("--metadata-only" or "--navigation-only" or "--pending-evidence-only" or "--overlay-availability-only" or "--hover-continuity-only")) return 2;
        _state = Path.GetFullPath(args[1]);
        // Every run owns a fresh directory; no existing browser profile is opened or changed.
        if (Directory.Exists(_state) && Directory.EnumerateFileSystemEntries(_state).Any()) return 2;
        Directory.CreateDirectory(_state);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (WebResourceLauncher.IsAdministrator())
        {
            WriteResult(false, new InvalidOperationException("Normal desktop user required."));
            return 2;
        }
        var window = new WebResourceWindow(Path.Combine(_state, "app-state"))
        { ShowActivated = false, Left = -10000, Top = -10000, Width = 1440, Height = 1100 };
        window.Loaded += async (_, _) =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
                await Run(window, Path.GetFullPath(args[0]), timeout.Token, args.ElementAtOrDefault(2));
                WriteResult(true, null);
                window.Close();
                app.Shutdown(0);
            }
            catch (Exception error)
            {
                WriteResult(false, error);
                foreach (Window dialog in app.Windows.Cast<Window>().Where(w => w != window).ToArray()) dialog.Close();
                window.Close();
                app.Shutdown(1);
            }
        };
        return app.Run(window);
    }

    private static async Task Run(WebResourceWindow window, string componentsRoot, CancellationToken ct, string? mode)
    {
        Stage("generate local H264 and AAC fixture");
        var manager = new ComponentManager(componentsRoot);
        var paths = await manager.GetInstalledAsync(ct) ?? throw new InvalidOperationException("Pinned components missing.");
        var sample = Path.Combine(_state, "fixture.mp4");
        var generated = await ProcessRunner.RunAsync(paths.FFmpeg,
            ["-nostdin", "-n", "-v", "error", "-f", "lavfi", "-i", "testsrc=size=320x180:rate=10",
             "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "4", "-c:v", "libx264",
             "-pix_fmt", "yuv420p", "-c:a", "aac", "-movflags", "+faststart", sample], ct);
        Require(generated.ExitCode == 0 && File.Exists(sample), "Fixture generation failed.");
        Field(window, "_downloads").SetValue(window, new MediaDownloadService(manager));
        var output = Path.Combine(_state, "downloads");
        Directory.CreateDirectory(output);
        ((TextBox)window.FindName("OutputFolder")).Text = output;

        Stage("initialize actual app browser and overlay");
        var browser = (WebView2)window.FindName("Browser");
        await Until(() => browser.CoreWebView2 != null && !(bool)Field(window, "_browserInitializing").GetValue(window)!, ct);
        var core = browser.CoreWebView2;
        _runtime = core.Environment.BrowserVersionString;
        if (mode == "--hover-continuity-only")
        {
            Stage("hover preview continuity");
            await HoverContinuitySmoke.Run(window, core, await File.ReadAllBytesAsync(sample, ct), ct);
            Pass("trusted pointer card-to-overlay movement preserves hover and playback; leave, native confirmation, transforms and scrolling remain correct");
            return;
        }
        if (mode == "--pending-evidence-only")
        {
            Stage("same-document pending evidence");
            await PendingEvidenceSmoke.Run(window, core, await File.ReadAllBytesAsync(sample, ct), ct);
            Pass("pending same-document evidence survives SPA; raw rows and old-document responses remain excluded");
            return;
        }
        if (mode == "--overlay-availability-only")
        {
            Stage("native overlay readiness");
            await OverlayAvailabilitySmoke.Run(window, core, await File.ReadAllBytesAsync(sample, ct), ct);
            Pass("overlay readiness follows native binding and resets on player changes without automatic download");
            return;
        }
        if (mode == "--metadata-only")
        {
            Stage("metadata without content length binds synthetic blob player");
            await VerifyMetadataWithoutLength(window, core, await File.ReadAllBytesAsync(sample, ct), ct);
            return;
        }
        using var server = new FixtureServer(await File.ReadAllBytesAsync(sample, ct), ct);
        if (mode == "--navigation-only")
        {
            await VerifyNavigationResources(window, core, server, ct);
            return;
        }
        int userPopups = 0, automaticPopups = 0;
        core.NewWindowRequested += (_, e) =>
        {
            if (e.IsUserInitiated) userPopups++; else automaticPopups++;
        };
        core.Navigate(server.Origin + "fixture");
        var first = await ReadyDocument(core, "/first.mp4", ct);
        await Until(() => PlayerDocuments(window).ContainsKey(0) &&
            PlayerDocuments(window).Any(p => p.Key != 0 && p.Value.Players.Any(v => v.Visible)), ct);
        var visible = first.Players.Single(player => player.Visible);
        var hidden = first.Players.Single(player => !player.Visible);
        var observed = (Dictionary<string, WebResourceKind>)Field(window, "_playerObservedMedia").GetValue(window)!;
        await Until(() => observed.ContainsKey(visible.Source) && observed.ContainsKey(hidden.Source), ct);
        Require(PlayerMediaBinding.Resolve(first, visible, observed, new Dictionary<string, DouyinMediaItem>()) is { Choices.Count: 1 }, "Visible player did not bind exact observed source.");
        Require(PlayerMediaBinding.Resolve(first, hidden, observed, new Dictionary<string, DouyinMediaItem>()) == null, "Hidden preload became a candidate.");
        Require(await Evaluate<bool>(core, "[...document.querySelectorAll('[data-toolsbox-player]')].filter(h=>getComputedStyle(h).display!=='none').length===1"), "Overlay visibility did not match players.");
        Pass("actual app overlay installed; exact visible source bound; hidden preload excluded; iframe snapshot captured");

        Stage("bound video is automatically published before overlay click");
        await Until(() => Resources(window).Any(r => r.CapturedVideo && r.Url == visible.Source), ct);
        var published = Resources(window).Single(r => r.CapturedVideo && r.Url == visible.Source && r.PageUrl == first.Url);
        published.IsSelected = true;
        await Poll(window);
        Require(ReferenceEquals(published, Resources(window).Single(r => r.Url == published.Url && r.PageUrl == published.PageUrl)) && published.IsSelected,
            "Repeated polling duplicated or reset selected row.");
        Require(Tasks(window).Count == 0 && !Resources(window).Any(r => r.CapturedVideo && r.Url == hidden.Source), "Publication downloaded or promoted hidden preload.");
        published.IsSelected = false;
        Pass("visible named captured video appeared before overlay; repeated polling preserved row identity and selection without starting download");

        Stage("iframe cannot spoof top-level site in its snapshot");
        int frameId = PlayerDocuments(window).Keys.Single(id => id != 0);
        var frames = (Dictionary<int, CoreWebView2Frame>)Field(window, "_playerFrames").GetValue(window)!;
        var frame = frames[frameId];
        try
        {
            await frame.ExecuteScriptAsync("window.__smokeOriginal=window.__toolsboxPlayers;window.__toolsboxPlayers=v=>({...window.__smokeOriginal(v),url:'https://www.douyin.com/'})");
            var forged = await (Task<PlayerDocument?>)window.GetType().GetMethod("ReadPlayerDocument", Private)!.Invoke(window, [frameId])!;
            Require(forged == null, "Iframe forged snapshot URL was accepted.");
        }
        finally
        {
            await frame.ExecuteScriptAsync("window.__toolsboxPlayers=window.__smokeOriginal;delete window.__smokeOriginal");
        }
        Pass("iframe snapshot claiming a different site was rejected against actual document location");

        Stage("iframe navigation invalidates open native confirmation");
        var frameGenerations = (Dictionary<int, long>)Field(window, "_frameGenerations").GetValue(window)!;
        long originalFrameGeneration = frameGenerations[frameId];
        var staleFrameDialog = await OpenOverlayDialog(window, core, ct, frame: true);
        await core.ExecuteScriptAsync("document.querySelector('iframe').src='/frame?generation=2'");
        await Until(() => frameGenerations.GetValueOrDefault(frameId) > originalFrameGeneration, ct);
        Confirm(staleFrameDialog);
        await Until(() => !(bool)Field(window, "_confirmingPlayer").GetValue(window)!, ct);
        Require(Tasks(window).Count == 0, "Iframe navigation confirmation started a stale download.");
        Pass("trusted iframe overlay opened native dialog; iframe navigation generation rejected stale download");

        Stage("native overlay confirmation cancellation");
        var cancelDialog = await OpenOverlayDialog(window, core, ct);
        cancelDialog.DialogResult = false;
        await Until(() => !(bool)Field(window, "_confirmingPlayer").GetValue(window)!, ct);
        Require(Tasks(window).Count == 0, "Cancel added a queue entry.");
        Pass("trusted DOM overlay mouse click opened native dialog; cancel left queue empty");

        Stage("source replacement while native confirmation is open");
        await Task.Delay(2100, ct);
        var staleSourceDialog = await OpenOverlayDialog(window, core, ct);
        await core.ExecuteScriptAsync("document.querySelector('#main').src='/second.mp4';document.querySelector('#main').load()");
        var replaced = await ReadyDocument(core, "/second.mp4", ct);
        var replacement = replaced.Players.Single(player => player.Visible);
        Require(replacement.Id == visible.Id && replacement.Generation > visible.Generation, "Reused video element did not change generation.");
        Require(!PlayerMediaBinding.IsSameTarget(first, visible, replaced), "Source replacement retained stale identity.");
        Confirm(staleSourceDialog);
        await Until(() => !(bool)Field(window, "_confirmingPlayer").GetValue(window)!, ct);
        Require(Tasks(window).Count == 0, "Source replacement confirmation started a stale download.");
        Pass("same video element source and generation changed; stale native confirmation rejected");
        Require(!await (Task<bool>)window.GetType().GetMethod("ValidatePlayerResource", Private)!.Invoke(window, [published])!, "Old list binding survived player source change.");

        Stage("SPA navigation while native confirmation is open");
        await Task.Delay(2100, ct);
        var staleSpaDialog = await OpenOverlayDialog(window, core, ct);
        await core.ExecuteScriptAsync("history.pushState({},'', '/fixture-selected?video=second')");
        var spa = await Snapshot(core);
        Require(!PlayerMediaBinding.IsSameTarget(replaced, replacement, spa), "SPA URL change retained stale identity.");
        Confirm(staleSpaDialog);
        await Until(() => !(bool)Field(window, "_confirmingPlayer").GetValue(window)!, ct);
        Require(Tasks(window).Count == 0, "SPA confirmation started a stale download.");
        Pass("SPA URL update invalidated open native confirmation without full navigation");

        Stage("confirmed captured media actual download");
        await Task.Delay(2100, ct);
        var accepted = await OpenOverlayDialog(window, core, ct);
        Confirm(accepted);
        await Until(() => Tasks(window).Count == 1 && !Tasks(window)[0].IsActive, ct);
        var task = Tasks(window).Single();
        Require(task.Resource.CapturedVideo && task.Resource.Url.EndsWith("/second.mp4", StringComparison.Ordinal), "Native confirmation did not choose CapturedVideo current source.");
        Require(task.Succeeded && File.Exists(task.OutputPath), "Captured media download failed.");
        var probe = await ProcessRunner.RunAsync(paths.FFprobe,
            ["-v", "error", "-show_entries", "format=format_name,duration:stream=codec_type,codec_name", "-of", "json", task.OutputPath!], ct);
        Require(probe.ExitCode == 0, "Saved MP4 failed ffprobe.");
        using (var document = JsonDocument.Parse(probe.Output))
        {
            var root = document.RootElement;
            var format = root.GetProperty("format");
            _duration = double.Parse(format.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            var streams = root.GetProperty("streams").EnumerateArray().ToArray();
            _hasAudio = streams.Any(stream => stream.GetProperty("codec_type").GetString() == "audio" && stream.GetProperty("codec_name").GetString() == "aac");
            Require(format.GetProperty("format_name").GetString()!.Split(',').Contains("mp4") && Math.Abs(_duration.Value - 4) <= 0.1 && _hasAudio &&
                streams.Any(stream => stream.GetProperty("codec_type").GetString() == "video" && stream.GetProperty("codec_name").GetString() == "h264"), "Saved media lost duration, MP4 format, H264 video, or AAC audio.");
        }
        Require(Directory.EnumerateFiles(output, "*.mp4").Count() == 1 && !Directory.EnumerateDirectories(output, ".video-*").Any(), "Unexpected download output or leftover stage.");
        Pass("native confirmation routed CapturedVideo; actual saved MP4 verified as four-second H264 plus AAC");

        Stage("automatic popup rejection without transient user activation");
        // ExecuteScriptAsync itself can confer user activation. A fresh page's load handler cannot.
        string beforeAutomatic = server.Origin + "automatic-check";
        int previousAutomaticPopups = automaticPopups;
        await Task.Delay(6500, ct);
        await NavigateWithoutScript(core, beforeAutomatic, ct);
        var automatic = await Evaluate<bool[]?>(core, "window.__smokeAutomatic ?? null");
        Require(automatic is { Length: 2 } && !automatic[0] && !automatic[1], "Automatic popup still had user activation or opened another window.");
        Require(core.Source == beforeAutomatic && ReferenceEquals(core, browser.CoreWebView2), "Automatic popup replaced browser source.");
        Pass(automaticPopups > previousAutomaticPopups ? "fresh page load window.open reached native policy as non-user-initiated and was blocked" :
            "fresh page load window.open had no activation and was blocked by browser before native popup event");

        Stage("trusted target blank opens in same browser and retains history");
        // History gets its own page so Back cannot rerun the automatic-popup fixture's load handler.
        string beforePopup = server.Origin + "popup-history";
        await NavigateWithoutScript(core, beforePopup, ct);
        await ReadyDocument(core, "/first.mp4", ct);
        await Click(core, "document.querySelector('#popup')", ct);
        await Until(() => core.Source == server.Origin + "target", ct);
        Require(userPopups > 0 && ReferenceEquals(core, browser.CoreWebView2), "Trusted target blank did not reuse current browser.");
        await Until(() => core.CanGoBack, ct);
        core.GoBack();
        // Back may restore the cached SPA document or reload its fixture HTML.
        await ReadyDocument(core, ".mp4", ct);
        Require(core.Source == beforePopup && ReferenceEquals(core, browser.CoreWebView2), "Back did not restore the prior document in same CoreWebView2.");
        Pass("real mouse target=_blank was user-initiated; same CoreWebView2 navigation and Back succeeded");

        Stage("native list download rejects stale resource before starting media work");
        // A retained task/action may still hold an old row even after navigation removes it from the list.
        window.GetType().GetMethod("StartDownload", Private)!.Invoke(window, [new WebDownloadRow(published, output)]);
        await Until(() => Tasks(window).Count == 2 && !Tasks(window)[1].IsActive, ct);
        Require(!Tasks(window)[1].Succeeded && Tasks(window)[1].Status.Contains("播放器已切换") && Directory.EnumerateFiles(output, "*.mp4").Count() == 1,
            "Stale list click started media download or returned unrelated error.");
        Pass("retained action on previous player rejected stale binding before cookie/component/media operations");

        await VerifyNavigationResources(window, core, server, ct);

        Stage("metadata without content length binds synthetic blob player");
        await VerifyMetadataWithoutLength(window, core, await File.ReadAllBytesAsync(sample, ct), ct);
        await VerifyBilibiliBinding(window, core, await File.ReadAllBytesAsync(sample, ct), ct);
    }

    private static FieldInfo Field(WebResourceWindow window, string name) => window.GetType().GetField(name, Private) ?? throw new MissingFieldException(name);
    private static ObservableCollection<WebDownloadRow> Tasks(WebResourceWindow window) => (ObservableCollection<WebDownloadRow>)Field(window, "_tasks").GetValue(window)!;
    private static ObservableCollection<WebResourceRow> Resources(WebResourceWindow window) => (ObservableCollection<WebResourceRow>)Field(window, "_resources").GetValue(window)!;
    private static Task Poll(WebResourceWindow window) => (Task)window.GetType().GetMethod("PollPlayers", Private)!.Invoke(window, null)!;
    private static Dictionary<int, PlayerDocument> PlayerDocuments(WebResourceWindow window) => (Dictionary<int, PlayerDocument>)Field(window, "_playerDocuments").GetValue(window)!;

    private static async Task VerifyNavigationResources(WebResourceWindow window, CoreWebView2 core, FixtureServer server, CancellationToken ct)
    {
        void Trace(string text) => File.AppendAllText(Path.Combine(_state, "navigation-events.txt"), text + Environment.NewLine);
        core.NavigationStarting += (_, e) => Trace("starting:" + new Uri(e.Uri).AbsolutePath);
        core.SourceChanged += (_, e) => Trace("source:new=" + e.IsNewDocument + ":" + new Uri(core.Source).AbsolutePath);
        core.NavigationCompleted += (_, e) => Trace("completed:" + e.IsSuccess);
        core.WebResourceResponseReceived += (_, e) => Trace("response:" + new Uri(e.Request.Uri).AbsolutePath + ":" + e.Response.StatusCode);
        Stage("refresh discards previous resource rows but keeps tasks");
        await NavigateWithoutScript(core, server.Origin + "navigation-fixture", ct);
        await ReadyDocument(core, "/first.mp4", ct);
        await Until(() => Resources(window).Any(r => r.CapturedVideo && r.Url.EndsWith("/first.mp4")), ct);
        var oldRows = Resources(window).ToArray();
        foreach (var row in oldRows) row.IsSelected = true;
        var retained = new WebDownloadRow(oldRows[0], _state) { IsActive = false, Status = "保留的测试任务" };
        Tasks(window).Add(retained);
        try
        {
            var beforeRefresh = await Snapshot(core);
            core.Reload();
            await Until(() => PlayerDocuments(window).TryGetValue(0, out var doc) && doc.Document != beforeRefresh.Document, ct);
            await Poll(window);
            Require(!Resources(window).Any(oldRows.Contains), "Reload retained rows from the previous document.");
            Require(Resources(window).All(r => !r.IsSelected), "Reload inherited previous resource selection.");
            Require(Tasks(window).Contains(retained) && !retained.Cancellation.IsCancellationRequested, "Navigation cleared or cancelled a download task.");
            Pass("same-URL refresh creates fresh unselected resources and preserves download tasks");

            Stage("SPA route switch discards old rows and rebinds current player");
            await core.ExecuteScriptAsync("window.__lateDone=false;fetch('/late.png').then(r=>r.arrayBuffer()).finally(()=>window.__lateDone=true);void 0");
            await server.LateResourceStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            oldRows = Resources(window).ToArray();
            await core.ExecuteScriptAsync("document.querySelector('iframe')?.remove();history.pushState({},'', '/navigation-spa?video=current')");
            await Until(() => (string)Field(window, "_page").GetValue(window)! == server.Origin + "navigation-spa?video=current", ct);
            Trace("spa-source-ready");
            await Poll(window);
            Trace("spa-polled:rows=" + Resources(window).Count + ";observed=" + ((Dictionary<string, WebResourceKind>)Field(window, "_playerObservedMedia").GetValue(window)!).Count);
            Trace("spa-observed:" + string.Join(',', ((Dictionary<string, WebResourceKind>)Field(window, "_playerObservedMedia").GetValue(window)!).Keys.Select(url => new Uri(url).AbsolutePath)));
            var spaDocument = await Snapshot(core);
            Trace("spa-players:" + string.Join(';', spaDocument.Players.Select(p => $"visible={p.Visible},source={new Uri(p.Source).AbsolutePath},duration={p.Duration}")));
            Require(!Resources(window).Any(oldRows.Contains), "SPA route change retained previous-page resources.");
            await Until(() => Resources(window).Any(r => r.CapturedVideo && r.PageUrl == core.Source), ct);
            Pass("SPA URL change clears previous-page rows while retaining evidence to bind the still-visible player");

            Stage("old page response arriving after SPA navigation is not relabelled as current");
            server.ReleaseLateResource.TrySetResult();
            for (int i = 0; i < 100 && !await Evaluate<bool>(core, "window.__lateDone===true"); i++) await Task.Delay(50, ct);
            Require(await Evaluate<bool>(core, "window.__lateDone===true"), "Delayed response did not complete.");
            await Task.Delay(250, ct);
            Require(!Resources(window).Any(r => r.Url.EndsWith("/late.png")), "Previous-page response was added to the new SPA page.");
            Pass("request started on the old route is discarded even when its response arrives after the route changed");

            Stage("slow top-level navigation cannot republish the previous document");
            int oldDocumentRequests = 0;
            void OldDocumentRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
            { if (e.Request.Uri.Contains("/during-nav.png?") && (string)Field(window, "_page").GetValue(window)! == server.Origin + "slow-target") oldDocumentRequests++; }
            core.WebResourceRequested += OldDocumentRequest;
            await core.ExecuteScriptAsync("window.__navTick=0;setInterval(()=>fetch('/during-nav.png?sequence='+(++window.__navTick),{keepalive:true}).catch(()=>{}),80)");
            core.Navigate(server.Origin + "slow-target");
            await server.SlowNavigationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            // ExecuteScriptAsync may wait until the new document is ready; never await it while holding that document.
            var pendingPoll = Poll(window);
            await Task.Delay(750, ct);
            Require(Resources(window).Count == 0, "Old player resources reappeared while the next document was loading.");
            server.ReleaseSlowNavigation.TrySetResult();
            await pendingPoll.WaitAsync(TimeSpan.FromSeconds(10), ct);
            await Until(() => core.Source == server.Origin + "slow-target", ct);
            await Task.Delay(500, ct);
            core.WebResourceRequested -= OldDocumentRequest;
            Require(oldDocumentRequests > 0, "Old document did not issue requests during pending navigation.");
            await Poll(window);
            Require(Resources(window).Count == 0, "Resource-free destination retained previous-page resources.");
            Pass("navigation start clears immediately and prevents old DOM republishing while the destination loads");

            Stage("back and forward restore only current-page resources");
            core.GoBack();
            await Until(() => core.Source == server.Origin + "navigation-spa?video=current", ct);
            await ReadyDocument(core, "/first.mp4", ct);
            await Until(() => Resources(window).Any(r => r.CapturedVideo && r.PageUrl == core.Source), ct);
            core.GoForward();
            await Until(() => core.Source == server.Origin + "slow-target", ct);
            await Poll(window);
            Require(Resources(window).Count == 0, "Forward navigation retained resources from the previous page.");
            Require(Tasks(window).Contains(retained), "History navigation removed existing tasks.");
            Pass("back repopulates current resources; forward clears them without removing tasks");
        }
        finally { server.ReleaseSlowNavigation.TrySetResult(); server.ReleaseLateResource.TrySetResult(); Tasks(window).Remove(retained); }
    }

    private static async Task VerifyMetadataWithoutLength(WebResourceWindow window, CoreWebView2 core, byte[] sample, CancellationToken ct)
    {
        const string origin = "https://www.douyin.com";
        const string metadataPath = "/aweme/v1/web/aweme/detail/";
        bool sawMetadataWithoutLength = false;
        int partialMediaResponses = 0;
        var streams = new List<Stream>();
        core.AddWebResourceRequestedFilter(origin + "/*", CoreWebView2WebResourceContext.All);
        void Requested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            // Every request on this synthetic origin is fulfilled in memory. No real site is contacted.
            var path = new Uri(args.Request.Uri).AbsolutePath;
            string mime;
            byte[] content;
            int status = 200;
            if (path == metadataPath)
            {
                mime = "application/json";
                content = Encoding.UTF8.GetBytes("""
                    {"aweme_detail":{"aweme_id":"123","desc":"Synthetic blob fixture","video":{"duration":4000,"height":180,"width":320,"play_addr":{"url_list":["https://www.douyin.com/fixture-media.mp4"]}}}}
                    """);
            }
            else if (path == "/fixture-media.mp4")
            {
                mime = "video/mp4"; content = sample;
                if (args.Request.Headers.Contains("Range")) status = 206;
            }
            else if (path == "/video/123")
            {
                mime = "text/html; charset=utf-8";
                content = Encoding.UTF8.GetBytes("""
                    <!doctype html><title>Synthetic blob fixture</title><style>body{margin:0}video{display:block}</style>
                    <div class="video_123"><video id="blob-player" width="320" height="180" muted autoplay loop></video></div>
                    <script>(async()=>{const media=await (await fetch('/fixture-media.mp4')).blob();document.querySelector('video').src=URL.createObjectURL(media);await (await fetch('/aweme/v1/web/aweme/detail/')).json();})()</script>
                    """);
            }
            else { mime = "text/plain"; content = []; status = 404; }
            var stream = new MemoryStream(content, writable: false);
            streams.Add(stream);
            args.Response = core.Environment.CreateWebResourceResponse(stream, status,
                status == 200 ? "OK" : status == 206 ? "Partial Content" : "Not Found",
                "Content-Type: " + mime + "\r\nCache-Control: no-store" +
                (status == 206 ? $"\r\nContent-Range: bytes 0-{sample.Length - 1}/{sample.Length}" : ""));
        }
        void Responded(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
        {
            if (args.Request.Uri == origin + metadataPath)
                _metadataWithoutLength = sawMetadataWithoutLength = !args.Response.Headers.Contains("Content-Length");
            if (args.Request.Uri == origin + "/fixture-media.mp4" && args.Response.StatusCode == 206)
                partialMediaResponses++;
        }
        core.WebResourceRequested += Requested;
        core.WebResourceResponseReceived += Responded;
        try
        {
            await NavigateWithoutScript(core, origin + "/video/123", ct);
            var document = await ReadyDocument(core, "", ct);
            var catalog = (Dictionary<string, DouyinMediaItem>)Field(window, "_douyinMedia").GetValue(window)!;
            await Until(() =>
            {
                _metadataCount = catalog.Count;
                string status = (string?)Field(window, "_lastPlayerMetadataResult").GetValue(window) ?? "";
                if (status != _metadataStatus)
                {
                    _metadataStatus = status;
                    File.WriteAllText(Path.Combine(_state, "metadata-observation.json"), JsonSerializer.Serialize(new
                    { HeaderAbsent = _metadataWithoutLength, CatalogCount = _metadataCount, Status = status }, Json));
                }
                if (status.EndsWith("Exception", StringComparison.Ordinal))
                    throw new InvalidOperationException("Metadata body read failed: " + status);
                return sawMetadataWithoutLength && catalog.ContainsKey("123");
            }, ct);
            var player = document.Players.Single(p => p.Visible);
            Require(player.Source.StartsWith("blob:", StringComparison.Ordinal) && player.SiteId == "123", "Synthetic blob player identity was not detected.");
            var observed = (Dictionary<string, WebResourceKind>)Field(window, "_playerObservedMedia").GetValue(window)!;
            var bound = PlayerMediaBinding.Resolve(document, player, observed, catalog);
            Require(bound is { Choices.Count: 1 } && bound.Choices[0].Url == origin + "/fixture-media.mp4", "Metadata without Content-Length failed exact blob binding.");
            Pass("in-memory site fixture metadata omitted Content-Length; app catalog populated and blob player bound exact video ID");
            await Until(() => Resources(window).Any(r => r.CapturedVideo && r.PageUrl == document.Url && r.Name == "Synthetic blob fixture"), ct);
            Pass("Douyin blob metadata automatically published named downloadable row without overlay action");
            window.GetType().GetMethod("ClearResources", Private)!.Invoke(window, [window, new RoutedEventArgs()]);
            await Task.Delay(1100, ct); await Poll(window);
            Require(Resources(window).Count == 0, "Cleared player resources immediately reappeared.");
            Pass("clearing list suppresses same bound media across subsequent automatic polls");
            observed.Remove(origin + "/fixture-media.mp4");
            await core.ExecuteScriptAsync("fetch('/fixture-media.mp4',{headers:{Range:'bytes=0-'}}).then(r=>r.arrayBuffer())");
            await Until(() => partialMediaResponses > 0, ct);
            await Poll(window);
            Require(observed.ContainsKey(origin + "/fixture-media.mp4"), "Cleared media stopped updating observed network sources.");
            Require(Resources(window).Count == 0, "Cleared player resource reappeared after a new 206 response for the same media URL.");
            Pass("new 206 response for cleared media still updates observed sources without restoring a raw list row");
        }
        finally
        {
            core.WebResourceRequested -= Requested;
            core.WebResourceResponseReceived -= Responded;
            core.RemoveWebResourceRequestedFilter(origin + "/*", CoreWebView2WebResourceContext.All);
            foreach (var stream in streams) stream.Dispose();
        }
    }

    private static async Task VerifyBilibiliBinding(WebResourceWindow window, CoreWebView2 core, byte[] sample, CancellationToken ct)
    {
        Stage("Bilibili active episode DOM binding and ambiguity guards");
        const string origin = "https://www.bilibili.com";
        var streams = new List<Stream>();
        core.AddWebResourceRequestedFilter(origin + "/*", CoreWebView2WebResourceContext.All);
        void Requested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            bool media = new Uri(args.Request.Uri).AbsolutePath == "/fixture.mp4";
            byte[] content = media ? sample : Encoding.UTF8.GetBytes("""
                <!doctype html><style>body{margin:0}video{display:block}</style><title>单集测试</title>
                <div class="bpx-player-primary-area"><div class="bpx-player-video-area"><div class="bpx-player-video-wrap"><video width="320" height="180" muted autoplay loop></video></div></div></div>
                <a id="episode" class="EpisodeVirtualList_activeNumber__Sm49O" href="/bangumi/play/ep323085">1</a>
                <script>(async()=>{document.querySelector('video').src=URL.createObjectURL(await(await fetch('/fixture.mp4')).blob())})()</script>
                """);
            var stream = new MemoryStream(content, false); streams.Add(stream);
            args.Response = core.Environment.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: " + (media ? "video/mp4" : "text/html; charset=utf-8"));
        }
        core.WebResourceRequested += Requested;
        try
        {
            await NavigateWithoutScript(core, origin + "/bangumi/play/ss33415", ct);
            var before = await ReadyDocument(core, "", ct);
            var player = before.Players.Single(p=>p.Visible);
            Require(player.SiteId == "323085", "Bilibili active episode DOM not identified.");
            await Until(() => Resources(window).Any(r=>r.Url == origin+"/bangumi/play/ep323085"), ct);
            var row = Resources(window).Single(r=>r.Url == origin+"/bangumi/play/ep323085");
            Require(!row.CapturedVideo && row.ExpectedDuration is > 3.9 and < 4.1, "Episode page misclassified as direct complete media.");
            await core.ExecuteScriptAsync("document.querySelector('#episode').href='/bangumi/play/ep323086'");
            var after = await Snapshot(core);
            Require(after.Players[0].SiteId == "323086" && after.Players[0].Generation > player.Generation, "Episode change did not invalidate generation.");
            Require(!await (Task<bool>)window.GetType().GetMethod("ValidatePlayerResource", Private)!.Invoke(window, [row])!, "Episode change retained old list binding.");
            await core.ExecuteScriptAsync("document.body.append(document.querySelector('#episode').cloneNode(true))");
            Require((await Snapshot(core)).Players[0].SiteId == "", "Ambiguous active episodes were accepted.");
            await core.ExecuteScriptAsync("document.querySelectorAll('#episode')[1].remove();Object.defineProperty(document.querySelector('video'),'mediaKeys',{configurable:true,value:{}})");
            Require((await Snapshot(core)).Players[0].Encrypted, "Existing mediaKeys was not detected.");
            Pass("Bilibili season identifies active episode; list uses page extraction; episode switch, duplicate active links, and preexisting mediaKeys are guarded");
        }
        finally
        {
            core.WebResourceRequested -= Requested;
            core.RemoveWebResourceRequestedFilter(origin + "/*", CoreWebView2WebResourceContext.All);
            foreach (var stream in streams) stream.Dispose();
        }
    }

    private static async Task<PlayerDocument> Snapshot(CoreWebView2 core) =>
        JsonSerializer.Deserialize<PlayerDocument>(await core.ExecuteScriptAsync("window.__toolsboxPlayers?.() ?? null"), Json) ?? new();

    private static async Task<PlayerDocument> ReadyDocument(CoreWebView2 core, string suffix, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 300; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var document = await Snapshot(core);
            if (document.Players.Any(player => player.Visible && player.Source.EndsWith(suffix, StringComparison.Ordinal) && player.Duration is > 3.9 and < 4.1)) return document;
            await Task.Delay(100, ct);
        }
        throw new TimeoutException("Playable document did not appear.");
    }

    private static async Task<Window> OpenOverlayDialog(WebResourceWindow window, CoreWebView2 core, CancellationToken ct, bool frame = false)
    {
        var lastMessage = (DateTime)Field(window, "_lastPlayerMessage").GetValue(window)!;
        double remaining = 2100 - (DateTime.UtcNow - lastMessage).TotalMilliseconds;
        if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), ct);
        await (Task)window.GetType().GetMethod("PollPlayers", Private)!.Invoke(window, null)!;
        await Click(core, frame ? "document.querySelector('iframe').contentDocument.querySelector('[data-toolsbox-player]').shadowRoot.querySelector('button')" :
            "[...document.querySelectorAll('[data-toolsbox-player]')].find(h=>getComputedStyle(h).display!=='none').shadowRoot.querySelector('button')", ct, frame);
        await Until(() => Application.Current.Windows.Cast<Window>().Any(w => w.GetType().Name == "PlayerDownloadDialog"), ct);
        var dialog = Application.Current.Windows.Cast<Window>().Single(w => w.GetType().Name == "PlayerDownloadDialog");
        Require(dialog.IsVisible && dialog.Owner == window, "Expected native owned confirmation dialog.");
        return dialog;
    }

    private static void Confirm(Window dialog)
    {
        var panel = (StackPanel)dialog.Content;
        var buttons = panel.Children.OfType<StackPanel>().Single();
        buttons.Children.OfType<Button>().Single(button => (string)button.Content == "确认下载 MP4")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task Click(CoreWebView2 core, string expression, CancellationToken ct, bool frame = false)
    {
        var point = await Evaluate<double[]>(core, "(()=>{const r=(" + expression + ").getBoundingClientRect();const o=" +
            (frame ? "document.querySelector('iframe').getBoundingClientRect()" : "{left:0,top:0}") + ";return [o.left+r.left+r.width/2,o.top+r.top+r.height/2]})()");
        ct.ThrowIfCancellationRequested();
        Require(point.Length == 2 && point.All(double.IsFinite), "Clickable target has no position.");
        foreach (string type in new[] { "mouseMoved", "mousePressed", "mouseReleased" })
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new
            { type, x = point[0], y = point[1], button = type == "mouseMoved" ? "none" : "left", clickCount = type == "mouseMoved" ? 0 : 1 }));
    }

    private static async Task<T> Evaluate<T>(CoreWebView2 core, string script) =>
        JsonSerializer.Deserialize<T>(await core.ExecuteScriptAsync(script), Json)!;

    private static async Task NavigateWithoutScript(CoreWebView2 core, string url, CancellationToken ct)
    {
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args) => loaded.TrySetResult(args.IsSuccess);
        core.NavigationCompleted += Completed;
        try
        {
            core.Navigate(url);
            Require(await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30), ct), "Fixture page navigation failed.");
        }
        finally { core.NavigationCompleted -= Completed; }
    }

    private static async Task Until(Func<bool> condition, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 400; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (condition()) return;
            await Task.Delay(100, ct);
        }
        throw new TimeoutException("Expected state did not appear.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Stage(string name)
    {
        _stage = name;
        File.WriteAllText(Path.Combine(_state, "progress.txt"), name);
    }

    private static void Pass(string description) => Checks.Add(description);

    private static void WriteResult(bool passed, Exception? error) => File.WriteAllText(Path.Combine(_state, "player-smoke-result.json"),
        JsonSerializer.Serialize(new
        {
            Passed = passed, Stage = _stage, Checks, Runtime = _runtime, DurationSeconds = _duration, HasAudio = _hasAudio,
            MetadataResponseWithoutLength = _metadataWithoutLength, MetadataCatalogCount = _metadataCount,
            MetadataReadStatus = _metadataStatus,
            ErrorType = error?.GetType().Name,
            // No browser URLs, cookies, response bodies, or request headers are included.
            Error = error is InvalidOperationException or TimeoutException ? error.Message : null
        }, Json));

    private sealed class FixtureServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop;
        private readonly Task _loop;
        private readonly byte[] _video;
        public string Origin { get; }
        public TaskCompletionSource SlowNavigationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSlowNavigation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LateResourceStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseLateResource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FixtureServer(byte[] video, CancellationToken ct)
        {
            _video = video;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            Origin = $"http://localhost:{port}/";
            _listener.Prefixes.Add(Origin);
            _listener.Start();
            _loop = Task.Run(Listen);
        }

        private async Task Listen()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync().WaitAsync(_stop.Token); }
                catch (Exception) when (_stop.IsCancellationRequested) { break; }
                await Serve(context);
            }
        }

        private async Task Serve(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url!.AbsolutePath;
                if (path is "/late.png" or "/during-nav.png")
                {
                    if (path == "/late.png")
                    {
                        LateResourceStarted.TrySetResult();
                        await ReleaseLateResource.Task.WaitAsync(TimeSpan.FromSeconds(15), _stop.Token);
                    }
                    var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
                    context.Response.ContentType = "image/png";
                    context.Response.ContentLength64 = png.Length;
                    await context.Response.OutputStream.WriteAsync(png, _stop.Token);
                    context.Response.Close();
                    return;
                }
                if (path == "/slow-target")
                {
                    SlowNavigationStarted.TrySetResult();
                    await ReleaseSlowNavigation.Task.WaitAsync(TimeSpan.FromSeconds(15), _stop.Token);
                }
                if (path.EndsWith(".mp4", StringComparison.Ordinal))
                {
                    long start = 0, end = _video.Length - 1;
                    string? range = context.Request.Headers["Range"];
                    if (range?.StartsWith("bytes=", StringComparison.Ordinal) == true)
                    {
                        var parts = range[6..].Split('-');
                        if (!long.TryParse(parts[0], out start) || start < 0 || start >= _video.Length)
                        { context.Response.StatusCode = 416; context.Response.Close(); return; }
                        if (parts.Length == 2 && long.TryParse(parts[1], out long requestedEnd)) end = Math.Min(end, requestedEnd);
                        context.Response.StatusCode = 206;
                        context.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{_video.Length}";
                    }
                    context.Response.Headers["Accept-Ranges"] = "bytes";
                    context.Response.Headers["Cache-Control"] = "no-store";
                    context.Response.ContentType = "video/mp4";
                    context.Response.ContentLength64 = end - start + 1;
                    if (context.Request.HttpMethod != "HEAD")
                        await context.Response.OutputStream.WriteAsync(_video.AsMemory((int)start, (int)(end - start + 1)), _stop.Token);
                }
                else
                {
                    string html = path switch
                    {
                        "/target" or "/slow-target" => "<!doctype html><title>Popup target</title><p>Same browser target</p>",
                        "/automatic" => "<!doctype html><title>Blocked automatic target</title>",
                        "/frame" => "<!doctype html><title>Frame fixture</title><style>body{margin:0}</style><video src='/frame.mp4' width='240' height='135' preload='auto' muted autoplay loop></video>",
                        _ => """
                            <!doctype html><html><head><title>Local player fixture</title>
                            <style>body{margin:0;padding:0;font-family:sans-serif}.row{display:flex;gap:14px;align-items:flex-start}video{display:block}iframe{border:0;width:250px;height:145px}a{display:block;width:210px;padding:10px;background:#ddd}</style></head>
                            <body><div class='row'><div><video id='main' src='/first.mp4' width='320' height='180' preload='auto' muted autoplay loop></video>
                            <a id='popup' href='/target' target='_blank'>Open target in current browser</a></div>
                            <iframe src='/frame'></iframe></div>
                            <video id='hidden' src='/hidden.mp4' style='display:none' preload='auto'></video></body></html>
                            """
                    };
                    if (path == "/automatic-check")
                        html += """<script>addEventListener('load',()=>{const active=navigator.userActivation.isActive;const opened=window.open('/automatic','_blank');window.__smokeAutomatic=[active,opened!==null]})</script>""";
                    var content = Encoding.UTF8.GetBytes(html);
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.ContentLength64 = content.Length;
                    await context.Response.OutputStream.WriteAsync(content, _stop.Token);
                }
                context.Response.Close();
            }
            catch { context.Response.Abort(); }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _listener.Close();
            // The accept loop is canceled; it never touches the WPF dispatcher.
            try { _loop.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            _stop.Dispose();
        }
    }
}
