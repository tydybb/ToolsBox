using System.IO;
using System.Text.Json;
using System.Collections.ObjectModel;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using ToolsBox.App.WebResources;
using ToolsBox.Core.WebResources;
using ToolsBox.MediaDownloads;
using System.Windows;
using System.Windows.Controls;
using System.Net.Http;

internal static partial class Program
{
    private static async Task ProbeFeed(WebResourceWindow window, string root, List<string> lines, string mode, string componentsRoot, CancellationToken ct)
    {
        var core = ((WebView2)window.FindName("Browser")).CoreWebView2;
        var routes = new HashSet<string>();
        core.WebResourceResponseReceived += async (_, e) =>
        {
            if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) && uri.Host == "www.douyin.com" && uri.AbsolutePath.StartsWith("/aweme/"))
            {
                routes.Add(uri.AbsolutePath);
                if (!DouyinMediaParser.IsMetadataResponse(e.Request.Uri) && uri.AbsolutePath is "/aweme/v2/web/module/feed/" or "/aweme/v1/web/multi/aweme/detail/")
                    try
                    {
                        using var stream = await e.Response.GetContentAsync();
                        if (stream == null) return;
                        using var reader = new StreamReader(stream);
                        string json = await reader.ReadToEndAsync(ct);
                        if (json.Length > 4 * 1024 * 1024) return;
                        using var body = JsonDocument.Parse(json);
                        var shapes = new HashSet<string>();
                        void Visit(JsonElement element, string path, int depth)
                        {
                            if (depth > 6 || shapes.Count > 80) return;
                            if (element.ValueKind == JsonValueKind.Object)
                            {
                                shapes.Add(path + ":" + string.Join(',', element.EnumerateObject().Select(p=>p.Name).Take(25)));
                                foreach (var prop in element.EnumerateObject()) if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) Visit(prop.Value, path + "." + prop.Name, depth + 1);
                            }
                            else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray().Take(1)) Visit(item, path+"[]", depth+1);
                        }
                        Visit(body.RootElement,"root",0);
                        lines.Add("metadata-shape=" + JsonSerializer.Serialize(new { path=uri.AbsolutePath, parsed=DouyinMediaParser.Parse(json).Count, shapes }));
                    }
                    catch (Exception ex) { lines.Add("metadata-shape-error=" + ex.GetType().Name); }
            }
        };
        if (mode == "feed-download")
        {
            window.GetType().GetField("_downloads", Private)!.SetValue(window, new MediaDownloadService(new ComponentManager(Path.GetFullPath(componentsRoot))));
            string output = Path.Combine(root,"downloads"); Directory.CreateDirectory(output);
            ((TextBox)window.FindName("OutputFolder")).Text = output;
        }
        core.Navigate(mode is "feed" or "feed-download" or "feed-diagnose" ? "https://www.douyin.com/jingxuan?modal_id=7680095187900697907" : "https://www.douyin.com/");
        if (mode == "recommend")
        {
            await Task.Delay(18000, ct);
            string link = await core.ExecuteScriptAsync("(() => {const e=Array.from(document.querySelectorAll('a')).find(e=>e.innerText.trim()==='推荐'); if(!e)return null;const r=e.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()");
            using var point = JsonDocument.Parse(link);
            if (point.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Recommend navigation not found.");
            foreach (string type in new[] { "mouseMoved", "mousePressed", "mouseReleased" })
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new { type,x=point.RootElement.GetProperty("x").GetDouble(),y=point.RootElement.GetProperty("y").GetDouble(),button="left",clickCount=1 }));
        }
        for (int step = 0; step < 6; step++)
        {
            await Task.Delay(step == 0 ? 25000 : 8000, ct);
            // Dismiss only ordinary closeable overlays, using their visible buttons.
            string tutorial = await core.ExecuteScriptAsync("(() => {const e=Array.from(document.querySelectorAll('button,span,div')).find(e=>e.children.length<=1&&e.textContent.trim()==='我知道了');if(!e)return null;const r=e.getBoundingClientRect();return r.width>0&&r.height>0?{x:r.left+r.width/2,y:r.top+r.height/2}:null;})()");
            using (var point = JsonDocument.Parse(tutorial))
                if (point.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (string type in new[] { "mousePressed", "mouseReleased" })
                        await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type, x = point.RootElement.GetProperty("x").GetDouble(), y = point.RootElement.GetProperty("y").GetDouble(), button = "left", clickCount = 1 }));
            if (await core.ExecuteScriptAsync("document.body.innerText.includes('登录后免费畅享高清视频')") == "true")
                foreach (string type in new[] { "mousePressed", "mouseReleased" })
                    await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type, x = 962, y = 167, button = "left", clickCount = 1 }));
            await Task.Delay(1000, ct);
            var doc = await (Task<PlayerDocument?>)window.GetType().GetMethod("ReadPlayerDocument", Private)!.Invoke(window, [0])!;
            var observed = (Dictionary<string, WebResourceKind>)window.GetType().GetField("_playerObservedMedia", Private)!.GetValue(window)!;
            var catalog = (Dictionary<string, DouyinMediaItem>)window.GetType().GetField("_douyinMedia", Private)!.GetValue(window)!;
            await (Task)window.GetType().GetMethod("PollPlayers", Private)!.Invoke(window, null)!;
            var rows = (ObservableCollection<WebResourceRow>)window.GetType().GetField("_resources", Private)!.GetValue(window)!;
            var active = doc?.Players.FirstOrDefault(p => p.Visible);
            var bound = active == null ? null : PlayerMediaBinding.Resolve(doc!,active,observed,catalog);
            if (bound != null)
            {
                for (int retry=0;retry<20 && !rows.Any(r=>r.CapturedVideo && bound.Choices.Any(c=>c.Url==r.Url));retry++) await Task.Delay(100,ct);
                if (!rows.Any(r=>r.CapturedVideo && bound.Choices.Any(c=>c.Url==r.Url))) throw new InvalidOperationException("Current bound feed video missing from resource list.");
                if (mode == "feed-diagnose" && step>=2)
                {
                    lines.Add("choice-evidence="+JsonSerializer.Serialize(bound.Choices.Select(c=>new{host=new Uri(c.Url).Host,scheme=new Uri(c.Url).Scheme,exactObserved=observed.ContainsKey(c.Url),samePath=observed.Keys.Count(u=>new Uri(u).Host==new Uri(c.Url).Host&&new Uri(u).AbsolutePath==new Uri(c.Url).AbsolutePath)})));
                    using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,UseDefaultCredentials=false}){Timeout=TimeSpan.FromSeconds(10)};
                    foreach(var choice in bound.Choices.GroupBy(c=>new Uri(c.Url).Host).Select(g=>g.First()).Take(4))
                    {
                        foreach(bool range in new[]{false,true})
                        {
                            using var request=new HttpRequestMessage(HttpMethod.Get,choice.Url);
                            request.Headers.Referrer=new Uri("https://www.douyin.com/");request.Headers.TryAddWithoutValidation("User-Agent",core.Settings.UserAgent);
                            if(range)request.Headers.TryAddWithoutValidation("Range","bytes=0-0");
                            try { using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
                                lines.Add($"http-diagnostic=host:{new Uri(choice.Url).Host},scheme:{new Uri(choice.Url).Scheme},range:{range},status:{(int)response.StatusCode},type:{response.Content.Headers.ContentType?.MediaType},length:{response.Content.Headers.ContentLength}"); }
                            catch(Exception ex){lines.Add("http-diagnostic-error="+ex.GetType().Name);}
                        }
                    }
                    File.WriteAllLines(Path.Combine(root,"feed-progress.txt"),lines);
                    return;
                }
            }
            lines.Add(JsonSerializer.Serialize(new { step, document = doc?.Document, pagePath = new Uri(core.Source).AbsolutePath,
                catalogCount = catalog.Count, metadata = window.GetType().GetField("_lastPlayerMetadataResult", Private)!.GetValue(window), routes = routes.ToArray(),
                players = doc?.Players.Select(p => new { p.Id, p.SiteId, p.Generation, p.Visible, p.Duration, sourceKind = p.Source.StartsWith("blob:") ? "blob" : p.Source.Length == 0 ? "empty" : "http",
                    sourceObserved = observed.ContainsKey(p.Source), metadataFound = catalog.ContainsKey(p.SiteId), metadataDuration = catalog.GetValueOrDefault(p.SiteId)?.DurationSeconds,
                    bound = PlayerMediaBinding.Resolve(doc, p, observed, catalog) != null }), videoRows = rows.Count(r => r.IsVideo), capturedRows = rows.Count(r => r.CapturedVideo) }));
            if (bound != null && mode == "feed-download")
            {
                string hit = await core.ExecuteScriptAsync("(() => {const b=Array.from(document.querySelectorAll('[data-toolsbox-player]')).filter(h=>h.style.display!=='none').map(h=>h.shadowRoot.querySelector('button')).find(b=>!b.disabled);if(!b)return null;const r=b.getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};})()");
                using var point=JsonDocument.Parse(hit);
                if(point.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("Bound video overlay unavailable.");
                foreach(string type in new[]{"mouseMoved","mousePressed","mouseReleased"})await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x=point.RootElement.GetProperty("x").GetDouble(),y=point.RootElement.GetProperty("y").GetDouble(),button=type=="mouseMoved"?"none":"left",clickCount=type=="mouseMoved"?0:1}));
                Window? dialog=null;
                for(int retry=0;retry<100 && dialog==null;retry++){await Task.Delay(100,ct);dialog=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w.GetType().Name=="PlayerDownloadDialog");}
                if(dialog==null)throw new InvalidOperationException("Feed overlay did not open native confirmation.");
                var candidate=dialog.GetType().GetProperty("Candidate")!.GetValue(dialog)!;
                var selected=(PlayerSnapshot)candidate.GetType().GetProperty("Player")!.GetValue(candidate)!;
                if(selected.SiteId!=active!.SiteId)throw new InvalidOperationException("Overlay selected a previous feed video.");
                lines.Add($"native-confirm-step={step};siteId={selected.SiteId};PASS");
                var tasks=(ObservableCollection<WebDownloadRow>)window.GetType().GetField("_tasks",Private)!.GetValue(window)!;
                if(step>=2 && tasks.Count==0)
                {
                    // Final acceptance uses the default choice actually presented to users.
                    var quality=(ComboBox)dialog.GetType().GetField("_quality",Private)!.GetValue(dialog)!;
                    quality.SelectedIndex=0;
                    var chosen=(PlayerMediaChoice)quality.SelectedItem;
                    lines.Add("download-choice-host="+new Uri(chosen.Url).Host);
                    dialog.DialogResult=true;
                    for(int retry=0;retry<100 && tasks.Count==0;retry++)await Task.Delay(100,ct);
                    while(tasks.Any(t=>t.IsActive))await Task.Delay(500,ct);
                    lines.Add("download-task-count="+tasks.Count);
                    if(tasks.Count>0)lines.Add("download-task-result="+tasks[0].Status);
                    if(tasks.Count==0)lines.Add("download-native-result="+((TextBlock)window.FindName("Status")).Text);
                    if(tasks.Count!=1||!tasks[0].Succeeded||!File.Exists(tasks[0].OutputPath))throw new InvalidOperationException("Later feed MP4 download failed.");
                    lines.Add($"later-video-mp4=PASS;siteId={selected.SiteId};duration={selected.Duration};bytes={new FileInfo(tasks[0].OutputPath!).Length}");
                }
                else dialog.DialogResult=false;
                await Task.Delay(500,ct);
            }
            // Safe DOM structure only: no signed media URLs, body text, storage or cookies.
            lines.Add("dom=" + await core.ExecuteScriptAsync("Array.from(document.querySelectorAll('video')).slice(0,32).map(v=>{const r=v.getBoundingClientRect();const ancestors=[];for(let e=v.parentElement,n=0;e&&n<9;e=e.parentElement,n++)ancestors.push({tag:e.tagName,cls:e.className,videos:e.querySelectorAll('video').length,e2e:e.getAttribute('data-e2e')});return {rect:{x:r.x,y:r.y,w:r.width,h:r.height},ready:v.readyState,paused:v.paused,ancestors};})"));
            File.WriteAllLines(Path.Combine(root, "feed-progress.txt"), lines);
            await using (var preview = File.Create(Path.Combine(root, $"feed-{step}.png"))) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, preview);
            if (mode == "home") break;
            string target = await core.ExecuteScriptAsync("(() => {const v=Array.from(document.querySelectorAll('video')).find(v=>{const r=v.getBoundingClientRect();return r.width>100&&r.height>100&&r.top>=0&&r.top<innerHeight});const r=v?.getBoundingClientRect();return r?{x:r.left+r.width/2,y:r.top+r.height/2}:{x:innerWidth/2,y:innerHeight/2};})()");
            using var targetPoint = JsonDocument.Parse(target);
            double targetX=targetPoint.RootElement.GetProperty("x").GetDouble(), targetY=targetPoint.RootElement.GetProperty("y").GetDouble();
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type="mouseMoved",x=targetX,y=targetY }));
            if (step % 2 == 0)
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type = "mouseWheel", x = targetX, y = targetY, deltaX = 0, deltaY = 650 }));
            else
                foreach (string type in new[] { "keyDown", "keyUp" })
                    await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(new { type, key = "ArrowDown", code = "ArrowDown", windowsVirtualKeyCode = 40 }));
        }
    }
}
