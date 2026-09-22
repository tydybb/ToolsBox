using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
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
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length is not (1 or 2) || WebResourceLauncher.IsAdministrator()) return 2;
        string root = Path.GetFullPath(args[0]);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) return 2;
        Directory.CreateDirectory(root);
        var lines = new List<string>(); bool passed = false;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new WebResourceWindow(root) { ShowActivated = false, Left = -10000, Top = -10000, Height = 1500 };
        window.Loaded += async (_, _) =>
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
                var browser = (WebView2)window.FindName("Browser");
                while (browser.CoreWebView2 == null || window.GetType().GetField("_playerTimer", Private)!.GetValue(window) == null)
                    await Task.Delay(100, deadline.Token);
                var core = browser.CoreWebView2;
                var reads = new List<Task>(); int segments = 0;
                core.WebResourceResponseReceived += (_, e) =>
                {
                    if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)) return;
                    if (uri.AbsolutePath.EndsWith(".m4s")) segments++;
                    if (uri.Host == "api.bilibili.com" && uri.AbsolutePath.Contains("/playurl"))
                    {
                        lines.Add("playback-api-path=" + uri.AbsolutePath);
                        reads.Add(ReadMetadata(e, lines, deadline.Token));
                    }
                };
                core.Navigate("https://www.bilibili.com/bangumi/play/ss33415?from_spmid=666.4.hotlist.0");
                await Task.Delay(TimeSpan.FromSeconds(25), deadline.Token);
                if (reads.Count > 0) await Task.WhenAll(reads).WaitAsync(deadline.Token);
                var doc = await (Task<PlayerDocument?>)window.GetType().GetMethod("ReadPlayerDocument", Private)!.Invoke(window, [0])!;
                lines.Add("player-count=" + doc?.Players.Length);
                lines.Add("player-states=" + string.Join(';', doc?.Players.Select(p => $"visible={p.Visible},source={(p.Source.StartsWith("blob:") ? "blob" : "other")},duration={p.Duration},encrypted={p.Encrypted},siteId={p.SiteId}") ?? []));
                lines.Add("observed-m4s=" + segments);
                lines.Add("dom-episode=" + await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('a[href*=\"/bangumi/play/ep\"]')).filter(a=>/active|selected/.test(a.className)).slice(0,3).map(a=>({href:a.href,cls:a.className,text:a.innerText.slice(0,60)}))"));
                lines.Add("dom-player-mediaKeys=" + await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('video')).map(v=>!!v.mediaKeys)"));
                if (doc?.Players.SingleOrDefault(p => p.Visible)?.SiteId != "323085")
                    throw new InvalidOperationException("Current episode was not identified.");
                var rows = (ObservableCollection<WebResourceRow>)window.GetType().GetField("_resources", Private)!.GetValue(window)!;
                const string episode = "https://www.bilibili.com/bangumi/play/ep323085";
                var row = rows.Single(r => r.Url == episode);
                if (row.CapturedVideo || row.ExpectedDuration is not > 1490) throw new InvalidOperationException("Incorrect download route.");
                lines.Add("auto-list=single-episode-page;captured=false");
                // A trusted mouse click goes through the actual DOM overlay and native confirmation.
                var point = JsonSerializer.Deserialize<double[]>(await core.ExecuteScriptAsync("(()=>{const h=[...document.querySelectorAll('[data-toolsbox-player]')].find(h=>getComputedStyle(h).display!=='none');const r=h.shadowRoot.querySelector('button').getBoundingClientRect();return [r.left+r.width/2,r.top+r.height/2]})()"))!;
                foreach (var type in new[] { "mouseMoved", "mousePressed", "mouseReleased" })
                    await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type, x=point[0], y=point[1], button=type=="mouseMoved"?"none":"left", clickCount=type=="mouseMoved"?0:1 }));
                await Until(() => app.Windows.Cast<Window>().Any(w => w.GetType().Name == "PlayerDownloadDialog"), deadline.Token);
                var dialog = app.Windows.Cast<Window>().Single(w => w.GetType().Name == "PlayerDownloadDialog");
                var choice = (PlayerMediaChoice)dialog.GetType().GetProperty("Choice")!.GetValue(dialog)!;
                if (choice.Url != episode || !choice.RequiresPageExtraction) throw new InvalidOperationException("Overlay selected a different episode.");
                dialog.DialogResult = false;
                lines.Add("overlay=exact-episode-native-dialog;cancelled");
                if (args.Length == 2)
                {
                    var manager = new ComponentManager(Path.GetFullPath(args[1]));
                    var components = await manager.GetInstalledAsync(deadline.Token) ?? throw new InvalidOperationException("Missing components.");
                    window.GetType().GetField("_downloads", Private)!.SetValue(window, new MediaDownloadService(manager));
                    string output = Path.Combine(root, "downloads"); Directory.CreateDirectory(output);
                    ((TextBox)window.FindName("OutputFolder")).Text = output;
                    foreach (var item in rows) item.IsSelected = ReferenceEquals(item, row);
                    window.GetType().GetMethod("DownloadSelected", Private)!.Invoke(window, [window, new RoutedEventArgs()]);
                    var tasks = (ObservableCollection<WebDownloadRow>)window.GetType().GetField("_tasks", Private)!.GetValue(window)!;
                    await Until(() => tasks.Count == 1, deadline.Token);
                    var task = tasks.Single();
                    task.PropertyChanged += (_, e) => { if(e.PropertyName == "Status") File.WriteAllText(Path.Combine(root,"progress.txt"),task.Status); };
                    await Until(() => !task.IsActive, deadline.Token);
                    lines.Add("download-status=" + task.Status);
                    if (!task.Succeeded || !File.Exists(task.OutputPath)) throw new InvalidOperationException("Episode download failed.");
                    var probe = await ProcessRunner.RunAsync(components.FFprobe, ["-v","error","-show_entries","format=format_name,duration:stream=codec_type,codec_name","-of","json",task.OutputPath!], deadline.Token);
                    using var payload = JsonDocument.Parse(probe.Output);
                    var format = payload.RootElement.GetProperty("format");
                    double duration = double.Parse(format.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
                    var streams = payload.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                    bool audio = streams.Any(s=>s.GetProperty("codec_type").GetString()=="audio"), video = streams.Any(s=>s.GetProperty("codec_type").GetString()=="video");
                    if (probe.ExitCode != 0 || !audio || !video || Math.Abs(duration-1494)>30 || !format.GetProperty("format_name").GetString()!.Contains("mp4")) throw new InvalidOperationException("Final media validation failed.");
                    lines.Add($"mp4=video+audio;duration={duration};bytes={new FileInfo(task.OutputPath!).Length}");
                    lines.Add("output="+task.OutputPath);
                }
                passed = true;
            }
            catch (Exception error) { lines.Add("error-type=" + error.GetType().Name); }
            finally
            {
                lines.Add("passed=" + passed); File.WriteAllLines(Path.Combine(root, "bangumi-result.txt"), lines);
                foreach (Window dialog in app.Windows.Cast<Window>().Where(w => w != window).ToArray()) dialog.Close();
                window.Close(); app.Shutdown(passed ? 0 : 1);
            }
        };
        return app.Run(window);
    }

    private static async Task Until(Func<bool> condition, CancellationToken token)
    { while (!condition()) await Task.Delay(100, token); }

    private static async Task ReadMetadata(CoreWebView2WebResourceResponseReceivedEventArgs e, List<string> lines, CancellationToken ct)
    {
        try
        {
            using var stream = await e.Response.GetContentAsync().WaitAsync(TimeSpan.FromSeconds(15), ct);
            if (stream == null) return;
            using var memory = new MemoryStream(); byte[] buffer = new byte[16384]; int count;
            while ((count = await stream.ReadAsync(buffer, ct)) > 0) { if (memory.Length + count > 4 * 1024 * 1024) return; memory.Write(buffer, 0, count); }
            using var payload = JsonDocument.Parse(memory.ToArray());
            Walk(payload.RootElement, "root", 0, lines);
        }
        catch (Exception error) { lines.Add("metadata-error=" + error.GetType().Name); }
    }
    private static void Walk(JsonElement value, string path, int depth, List<string> lines)
    {
        if (depth > 5 || lines.Count > 220) return;
        if (value.ValueKind == JsonValueKind.Object)
        {
            lines.Add(path + ":{" + string.Join(',', value.EnumerateObject().Select(p => p.Name)) + "}");
            foreach (var p in value.EnumerateObject())
            {
                if (p.Name.Contains("drm") || p.Name is "code" or "type" or "duration" or "timelength" or "quality" or "cid" or "ep_id" or "is_preview")
                    if (p.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) lines.Add(path + "." + p.Name + "=" + p.Value);
                if (p.Name is "data" or "result" or "video_info" or "dash" or "video" or "audio" or "durl") Walk(p.Value, path + "." + p.Name, depth + 1, lines);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        { lines.Add(path + ".count=" + value.GetArrayLength()); if (value.GetArrayLength() > 0) Walk(value[0], path + "[0]", depth + 1, lines); }
    }
}
