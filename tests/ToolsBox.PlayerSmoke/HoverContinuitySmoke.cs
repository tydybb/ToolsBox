using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using ToolsBox.App.WebResources;

internal static class HoverContinuitySmoke
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Origin = "https://hover-preview.example.test";

    public static async Task Run(WebResourceWindow window, CoreWebView2 core, byte[] sample, CancellationToken ct)
    {
        var streams = new List<Stream>();
        void Requested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (!e.Request.Uri.StartsWith(Origin + "/", StringComparison.Ordinal)) return;
            bool media = new Uri(e.Request.Uri).AbsolutePath.EndsWith(".mp4", StringComparison.Ordinal);
            byte[] bytes = media ? sample : Encoding.UTF8.GetBytes("""
                <!doctype html><title>Hover preview fixture</title>
                <style>body{margin:0;min-height:1800px}.card{width:320px;height:180px;margin:70px;border:4px solid #444;overflow:hidden}.card video{display:block;width:100%;height:100%}#card{position:relative;transform:translate(13px,9px) scale(.85);transform-origin:top left}#scroll{height:230px;overflow:auto;position:relative;margin:20px}#scroll .card{margin:30px}.space{height:400px}</style>
                <style>#card:fullscreen{transform:none;margin:0;border:0;width:100vw;height:100vh}#card:fullscreen>div{height:100%}#fullscreen{position:fixed;left:500px;top:10px}</style>
                <button id="fullscreen">Fullscreen fixture</button>
                <div id="card" class="card"><div><video muted loop preload="auto" src="/first.mp4"></video></div></div>
                <div id="scroll"><div class="space"></div><div id="second" class="card"><video muted loop preload="auto" src="/second.mp4"></video></div><div class="space"></div></div>
                <script>
                window.stats={};
                for(const card of document.querySelectorAll('.card')){
                    const s=window.stats[card.id]={enter:0,leave:0,click:0,press:0,hover:false,trusted:true};
                    card.addEventListener('mouseenter',e=>{s.enter++;s.hover=true;s.trusted&&=e.isTrusted;card.querySelector('video').play();});
                    card.addEventListener('mouseleave',e=>{s.leave++;s.hover=false;s.trusted&&=e.isTrusted;card.querySelector('video').pause();});
                    card.addEventListener('click',()=>s.click++);
                    card.addEventListener('pointerdown',()=>s.press++);
                }
                document.getElementById('fullscreen').onclick=()=>document.getElementById('card').requestFullscreen();
                </script>
                """);
            var stream = new MemoryStream(bytes, false); streams.Add(stream);
            e.Response = core.Environment.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: " + (media ? "video/mp4" : "text/html;charset=utf-8"));
        }
        core.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += Requested;
        try
        {
            core.Navigate(Origin + "/page");
            await Until(async () => await Eval<bool>(core,"!!document.querySelector('#card video') && document.querySelector('#card video').duration>0"),ct);
            await CheckCard("card",click:true);
            await core.ExecuteScriptAsync("document.querySelector('#scroll').scrollTop=410;void 0");
            await CheckCard("second",click:false);
            await core.ExecuteScriptAsync("document.querySelector('#scroll').scrollTop=430;void 0");
            await CheckCard("second",click:false);
            var fullScreenButton=await Eval<double[]>(core,"(()=>{const r=document.getElementById('fullscreen').getBoundingClientRect();return [r.left+r.width/2,r.top+r.height/2]})()");
            await Move(fullScreenButton[0],fullScreenButton[1]);
            foreach(string type in new[]{"mousePressed","mouseReleased"})await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x=fullScreenButton[0],y=fullScreenButton[1],button="left",clickCount=1}));
            await Until(async()=>await Eval<bool>(core,"document.fullscreenElement?.id==='card'"),ct);
            await CheckCard("card",click:true,leave:false);
            await core.ExecuteScriptAsync("document.exitFullscreen();void 0");
            await Until(async()=>await Eval<bool>(core,"!document.fullscreenElement"),ct);
            await CheckCard("card",click:false);
            foreach(string transform in new[]{"rotate(10deg)","skewX(8deg)","scaleX(-1)"})
            {
                await core.ExecuteScriptAsync($"document.getElementById('card').style.transform={JsonSerializer.Serialize(transform)};window.__toolsboxPlayers();void 0");
                Require(await Eval<bool>(core,"[...document.querySelectorAll('#card [data-toolsbox-player]')].every(h=>getComputedStyle(h).display==='none')"),"Unsupported card transform exposed a misplaced download button.");
            }
            await core.ExecuteScriptAsync("document.getElementById('card').style.transform='';window.__toolsboxPlayers();void 0");
            await CheckCard("card",click:false);

            async Task CheckCard(string id,bool click,bool leave=true)
            {
                string card=JsonSerializer.Serialize(id);
                await Move(5,5);
                var center=await Eval<double[]>(core,$"(()=>{{const r=document.getElementById({card}).querySelector('video').getBoundingClientRect();return [r.left+r.width/2,r.top+r.height/2]}})()");
                await Move(center[0],center[1]);
                await Until(async()=>await Eval<bool>(core,$"stats[{card}].hover && !document.getElementById({card}).querySelector('video').paused"),ct);
                await Until(async()=>await Eval<bool>(core,$"(()=>{{const r=document.getElementById({card}).querySelector('video').getBoundingClientRect();return [...document.querySelectorAll('[data-toolsbox-player]')].some(h=>{{const b=h.shadowRoot.querySelector('button'),p=b.getBoundingClientRect();return !b.disabled&&h.style.display!=='none'&&p.left>=r.left&&p.left<r.right&&p.top>=Math.max(0,r.top)&&p.top<r.bottom}})}})()"),ct);
                var point=await Eval<double[]>(core,$"(()=>{{const r=document.getElementById({card}).querySelector('video').getBoundingClientRect();const h=[...document.querySelectorAll('[data-toolsbox-player]')].find(h=>{{const p=h.shadowRoot.querySelector('button').getBoundingClientRect();return h.style.display!=='none'&&p.left>=r.left&&p.left<r.right&&p.top>=Math.max(0,r.top)&&p.top<r.bottom}});const p=h.shadowRoot.querySelector('button').getBoundingClientRect();return [p.left+p.width/2,p.top+p.height/2]}})()");
                Require(await Eval<bool>(core,$"(()=>{{const r=document.getElementById({card}).querySelector('video').getBoundingClientRect(),h=document.elementFromPoint({point[0].ToString(System.Globalization.CultureInfo.InvariantCulture)},{point[1].ToString(System.Globalization.CultureInfo.InvariantCulture)});return document.getElementById({card}).contains(h)&&h.hasAttribute('data-toolsbox-player')}})()"),"Overlay point was occluded or outside its original card.");
                int leaves=await Eval<int>(core,$"stats[{card}].leave");
                await Move(point[0],point[1]);
                await Task.Delay(650,ct);
                Require(await Eval<bool>(core,$"stats[{card}].hover && document.getElementById({card}).matches(':hover') && !document.getElementById({card}).querySelector('video').paused && stats[{card}].leave==={leaves} && stats[{card}].trusted"),"Moving onto the download overlay ended the card's real hover preview: "+id);
                if(click)
                {
                    foreach(string type in new[]{"mousePressed","mouseReleased"})await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x=point[0],y=point[1],button="left",clickCount=1}));
                    await Until(()=>Task.FromResult(Application.Current.Windows.Cast<Window>().Any(w=>w.GetType().Name=="PlayerDownloadDialog")),ct);
                    Require(await Eval<int>(core,$"stats[{card}].click")==0,"Download click triggered the site's card navigation.");
                    Require(await Eval<int>(core,$"stats[{card}].press")==0,"Download press triggered the site's player controls.");
                    Application.Current.Windows.Cast<Window>().Single(w=>w.GetType().Name=="PlayerDownloadDialog").DialogResult=false;
                    var tasks=(ObservableCollection<WebDownloadRow>)window.GetType().GetField("_tasks",Private)!.GetValue(window)!;
                    Require(tasks.Count==0,"Hover or cancelled confirmation started a download.");
                }
                if(leave)
                {
                    await Move(5,5);
                    await Until(async()=>await Eval<bool>(core,$"!stats[{card}].hover && document.getElementById({card}).querySelector('video').paused && stats[{card}].leave==={leaves+1}"),ct);
                }
            }
            async Task Move(double x,double y)=>await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type="mouseMoved",x,y,button="none"}));
        }
        finally
        {
            core.WebResourceRequested-=Requested;core.RemoveWebResourceRequestedFilter(Origin+"/*",CoreWebView2WebResourceContext.All);
            foreach(var stream in streams)stream.Dispose();
        }
    }
    private static async Task<T> Eval<T>(CoreWebView2 core,string script)=>JsonSerializer.Deserialize<T>(await core.ExecuteScriptAsync(script))!;
    private static async Task Until(Func<Task<bool>> check,CancellationToken ct)
    {for(int i=0;i<150;i++){if(await check())return;await Task.Delay(100,ct);}throw new TimeoutException("Hover fixture condition not reached.");}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
