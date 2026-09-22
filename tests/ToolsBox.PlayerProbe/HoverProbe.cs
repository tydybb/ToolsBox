using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ToolsBox.App.WebResources;
using ToolsBox.Core.WebResources;

internal static partial class Program
{
    private static async Task ProbeHover(WebResourceWindow window, string root, List<string> lines, CancellationToken ct)
    {
        var core = ((WebView2)window.FindName("Browser")).CoreWebView2;
        var tasks = (System.Collections.ObjectModel.ObservableCollection<WebDownloadRow>)window.GetType().GetField("_tasks", Private)!.GetValue(window)!;
        int initialTaskCount = tasks.Count;
        int loginCloseClicks = 0;
        lines.Add("hover-runtime=" + core.Environment.BrowserVersionString);
        core.Navigate("https://www.douyin.com/jingxuan");
        for (int i = 0; i < 80; i++)
        {
            await Task.Delay(500, ct);
            if (await core.ExecuteScriptAsync("!!document.body && location.pathname==='/jingxuan' && document.readyState!=='loading'") == "true") break;
        }
        await core.ExecuteScriptAsync(HoverProbeInstall);
        bool candidatesFound = false;
        for (int i = 0; i < 70; i++)
        {
            if (loginCloseClicks < 3 && await DismissHoverLogin(core, lines, ct)) loginCloseClicks++;
            string candidates = await core.ExecuteScriptAsync("window.__toolsboxHoverProbe.scan()");
            using var result = JsonDocument.Parse(candidates);
            if (result.RootElement.GetArrayLength() > 0)
            {
                lines.Add("hover-candidates=" + candidates); candidatesFound = true; break;
            }
            await Task.Delay(500, ct);
        }
        if (!candidatesFound)
        {
            lines.Add("hover-preview-found=false;reason=no-visible-unobscured-card");
            await CaptureHoverPreview(core, root, "hover-no-card", ct);
            return;
        }

        bool previewFound = false;
        for (int candidate = 0; candidate < 6; candidate++)
        {
            if (loginCloseClicks < 3 && await DismissHoverLogin(core, lines, ct)) loginCloseClicks++;
            string selected = await core.ExecuteScriptAsync("window.__toolsboxHoverProbe.pick(" + candidate + ")");
            if (!TryHoverPoint(selected, out double x, out double y)) continue;
            lines.Add("hover-selected=" + selected);
            string neutral = await core.ExecuteScriptAsync("({x:innerWidth-1,y:innerHeight-1})");
            if (TryHoverPoint(neutral, out double outsideX, out double outsideY)) await MoveHoverMouse(core, outsideX, outsideY, ct);
            await Task.Delay(150, ct);
            await core.ExecuteScriptAsync("window.__toolsboxHoverProbe.resetEvents();void 0");
            await MoveHoverMouse(core, x, y, ct);
            for (int i = 0; i < 35; i++)
            {
                await Task.Delay(250, ct);
                string state = await core.ExecuteScriptAsync("window.__toolsboxHoverProbe.snapshot('looking-for-preview')");
                using var data = JsonDocument.Parse(state);
                if (data.RootElement.GetProperty("video").ValueKind == JsonValueKind.Object &&
                    data.RootElement.GetProperty("video").GetProperty("sourceKind").GetString() is "blob" or "http")
                {
                    previewFound = true; break;
                }
            }
            if (previewFound) break;
            lines.Add("hover-attempt=" + await core.ExecuteScriptAsync("window.__toolsboxHoverProbe.snapshot('no-preview')"));
        }
        lines.Add("hover-preview-found=" + previewFound);
        if (!previewFound)
        {
            await CaptureHoverPreview(core, root, "hover-no-preview", ct);
            return;
        }

        // Let the app identify the actual visible preview, without starting or forcing playback.
        for (int i = 0; i < 40; i++)
        {
            await (Task)window.GetType().GetMethod("PollPlayers", Private)!.Invoke(window, null)!;
            if (await core.ExecuteScriptAsync("!!window.__toolsboxHoverProbe.overlayPoint()") == "true") break;
            await Task.Delay(250, ct);
        }
        await core.ExecuteScriptAsync("window.__toolsboxHoverProbe.markBefore();void 0");
        await RecordHoverState(window, core, root, lines, "before-overlay", ct);
        await RecordHoverListeners(core, lines, ct);
        string button = await core.ExecuteScriptAsync("window.__toolsboxHoverProbe.overlayPoint()");
        if (!TryHoverPoint(button, out double buttonX, out double buttonY))
        {
            lines.Add("hover-overlay-found=false");
            return;
        }
        lines.Add("hover-overlay-found=true");
        lines.Add("hover-overlay-point=" + button);
        await core.ExecuteScriptAsync("window.__toolsboxHoverProbe.resetEvents();window.__toolsboxHoverProbe.movedAt=performance.now();void 0");
        await MoveHoverMouse(core, buttonX, buttonY, ct);
        await RecordHoverState(window, core, root, lines, "after-overlay-immediate", ct);
        await Task.Delay(150, ct);
        await RecordHoverState(window, core, root, lines, "after-overlay-150ms", ct);
        await Task.Delay(1000, ct);
        await RecordHoverState(window, core, root, lines, "after-overlay-1150ms", ct);
        await Task.Delay(1500, ct);
        await RecordHoverState(window, core, root, lines, "after-overlay-2650ms", ct);
        string exit = await core.ExecuteScriptAsync("window.__toolsboxHoverProbe.exitPoint()");
        if (TryHoverPoint(exit, out double exitX, out double exitY))
        {
            lines.Add("hover-normal-exit-point=" + exit);
            await core.ExecuteScriptAsync("window.__toolsboxHoverProbe.resetEvents();window.__toolsboxHoverProbe.movedAt=performance.now();void 0");
            await MoveHoverMouse(core, exitX, exitY, ct);
            await RecordHoverState(window, core, root, lines, "after-normal-exit-immediate", ct);
            await Task.Delay(300, ct);
            await RecordHoverState(window, core, root, lines, "after-normal-exit-300ms", ct);
            await Task.Delay(1200, ct);
            await RecordHoverState(window, core, root, lines, "after-normal-exit-1500ms", ct);
        }
        else lines.Add("hover-normal-exit-point=unavailable");
        // These are observations, not an assertion that every site version uses the same handler.
        lines.Add("hover-probe-completed=true;overlay-clicks=0;login-close-clicks=" + loginCloseClicks + ";download-task-delta=" + (tasks.Count - initialTaskCount));
    }

    private static async Task<bool> DismissHoverLogin(CoreWebView2 core, List<string> lines, CancellationToken ct)
    {
        string locator = await core.ExecuteScriptAsync("window.__toolsboxHoverProbe.loginClosePoint()");
        if (!TryHoverPoint(locator, out double x, out double y)) return false;
        lines.Add("hover-login-close-locator=" + locator);
        await MoveHoverMouse(core, x, y, ct);
        foreach (string type in new[] { "mousePressed", "mouseReleased" })
        {
            ct.ThrowIfCancellationRequested();
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type, x, y, button = "left", clickCount = 1 }));
        }
        await Task.Delay(500, ct);
        lines.Add("hover-login-prompt-still-visible=" + await core.ExecuteScriptAsync("window.__toolsboxHoverProbe.loginVisible()"));
        return true;
    }

    private static async Task RecordHoverState(WebResourceWindow window, CoreWebView2 core, string root, List<string> lines, string phase, CancellationToken ct)
    {
        lines.Add("hover-state=" + await core.ExecuteScriptAsync("window.__toolsboxHoverProbe.snapshot(" + JsonSerializer.Serialize(phase) + ")"));
        var doc = await (Task<PlayerDocument?>)window.GetType().GetMethod("ReadPlayerDocument", Private)!.Invoke(window, [0])!;
        lines.Add("hover-native=" + JsonSerializer.Serialize(new
        {
            phase,
            players = doc?.Players.Where(p => p.Visible).Select(p => new
            {
                p.Id, p.Generation, p.SiteId, p.Duration, p.Encrypted, p.StreamObject,
                sourceKind = p.Source.StartsWith("blob:", StringComparison.Ordinal) ? "blob" : p.Source.Length == 0 ? "empty" : "http"
            })
        }));
        await File.WriteAllLinesAsync(Path.Combine(root, "hover-progress.txt"), lines, ct);
        await CaptureHoverPreview(core, root, "hover-" + phase, ct);
    }

    private static async Task RecordHoverListeners(CoreWebView2 core, List<string> lines, CancellationToken ct)
    {
        // Record event names/options only; never emit function bodies, object values, or React state.
        for (int index = 0; index < 14; index++)
        {
            ct.ThrowIfCancellationRequested();
            string value = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
            {
                expression = "window.__toolsboxHoverProbe.tracked[" + index + "]?.element",
                objectGroup = "toolsbox-hover-diagnostic"
            }));
            using var evaluation = JsonDocument.Parse(value);
            if (!evaluation.RootElement.GetProperty("result").TryGetProperty("objectId", out var id)) break;
            string events = await core.CallDevToolsProtocolMethodAsync("DOMDebugger.getEventListeners", JsonSerializer.Serialize(new { objectId = id.GetString() }));
            using var listeners = JsonDocument.Parse(events);
            lines.Add("hover-direct-listeners=" + JsonSerializer.Serialize(new
            {
                index,
                includesProbeMouseEnterLeave = true,
                types = listeners.RootElement.GetProperty("listeners").EnumerateArray().Select(item => new
                {
                    type = item.GetProperty("type").GetString(),
                    capture = item.GetProperty("useCapture").GetBoolean(),
                    passive = item.GetProperty("passive").GetBoolean()
                }).Where(item => item.type?.Contains("mouse", StringComparison.OrdinalIgnoreCase) == true).ToArray()
            }));
        }
        await core.CallDevToolsProtocolMethodAsync("Runtime.releaseObjectGroup", "{\"objectGroup\":\"toolsbox-hover-diagnostic\"}");
    }

    private static async Task CaptureHoverPreview(CoreWebView2 core, string root, string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await using var stream = File.Create(Path.Combine(root, name + ".png"));
        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
    }

    private static async Task MoveHoverMouse(CoreWebView2 core, double x, double y, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type = "mouseMoved", x, y, button = "none" }));
    }

    private static bool TryHoverPoint(string value, out double x, out double y)
    {
        using var point = JsonDocument.Parse(value);
        x = y = 0;
        if (point.RootElement.ValueKind != JsonValueKind.Object || !point.RootElement.TryGetProperty("x", out var px) ||
            !point.RootElement.TryGetProperty("y", out var py)) return false;
        x = px.GetDouble(); y = py.GetDouble();
        return double.IsFinite(x) && double.IsFinite(y);
    }

    private const string HoverProbeInstall = """
    (()=>{
      const h={tracked:[],events:[],candidates:[],card:null,anchor:null,video:null,beforeVideo:null,beforeSource:null,targetRect:null,movedAt:null};
      const safeToken=s=>typeof s==='string' && /^[A-Za-z_][A-Za-z0-9_-]{0,80}$/.test(s) ? s : '';
      const describe=e=>e instanceof Element ? {tag:e.tagName,classes:Array.from(e.classList).map(safeToken).filter(Boolean).slice(0,12),e2e:safeToken(e.getAttribute('data-e2e'))} : null;
      const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,w:r.width,h:r.height};};
      const shown=e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e);return r.width>=80&&r.height>=60&&r.left>=0&&r.top>=0&&r.right<=innerWidth&&r.bottom<=innerHeight&&s.display!=='none'&&s.visibility==='visible';};
      const reactEvents=e=>{const key=Object.keys(e).find(k=>k.startsWith('__reactProps$'));return key&&e[key]?Object.keys(e[key]).filter(k=>/^onMouse(Enter|Leave|Over|Out|Move)(Capture)?$/.test(k)&&typeof e[key][k]==='function'):[];};
      const loginHeading=()=>{
        if(!document.body?.innerText.includes('登录后免费畅享高清视频'))return null;
        return Array.from(document.querySelectorAll('h1,h2,h3,div,span,p')).find(e=>e.children.length<=1&&e.textContent.trim()==='登录后免费畅享高清视频'&&e.getBoundingClientRect().width>0&&getComputedStyle(e).visibility==='visible')||null;
      };
      h.loginVisible=()=>!!loginHeading();
      h.loginClosePoint=()=>{
        const heading=loginHeading();if(!heading)return null;
        let modal=heading;
        while(modal&&modal!==document.body){const r=modal.getBoundingClientRect();if(r.width>=400&&r.height>=250&&modal.innerText.includes('扫码登录')&&modal.innerText.includes('验证码登录'))break;modal=modal.parentElement;}
        if(!modal||modal===document.body)return null;
        const r=modal.getBoundingClientRect();
        const closePoint=e=>{
          if(!e||!modal.contains(e))return null;const b=e.getBoundingClientRect();
          if(b.width<=0||b.height<=0||b.width>80||b.height>80||b.left<r.right-100||b.top>r.top+100||b.left>=r.right||b.top<r.top)return null;
          const x=b.x+b.width/2,y=b.y+b.height/2,hit=document.elementFromPoint(x,y);
          if(!hit||!(e===hit||e.contains(hit)||hit.contains(e)))return null;
          return {x,y,node:describe(e)};
        };
        for(const e of modal.querySelectorAll('button,[role="button"],[aria-label],svg,[class*="close"],[class*="Close"],[data-e2e*="close"]')){
          const point=closePoint(e);if(point)return {...point,method:'login-heading-and-top-right-close-dom'};
        }
        // The reviewed guest screenshot locates the ordinary X at (962,167). Use it
        // only if the same visible login dialog contains a small top-right DOM target.
        const known=document.elementFromPoint(962,167),point=closePoint(known);
        return point?{...point,x:962,y:167,method:'reviewed-screenshot-with-dialog-dom-guard'}:null;
      };
      h.track=(e,role)=>{
        if(!(e instanceof Element))return;
        const existing=h.tracked.find(t=>t.element===e);
        if(existing){if(!existing.roles.includes(role))existing.roles.push(role);return;}
        if(h.tracked.length>=18)return;
        const entry={element:e,index:h.tracked.length,roles:[role],mouseenter:0,mouseleave:0,trustedEnter:0,trustedLeave:0,handlers:{}};h.tracked.push(entry);
        for(const type of ['mouseenter','mouseleave']){const handler=event=>{
          entry[type]++;if(event.isTrusted)entry[type==='mouseenter'?'trustedEnter':'trustedLeave']++;
          if(h.events.length<100)h.events.push({index:entry.index,type,trusted:event.isTrusted,timeMs:h.movedAt===null?null:performance.now()-h.movedAt,target:describe(event.target),related:describe(event.relatedTarget),x:event.clientX,y:event.clientY});
        };entry.handlers[type]=handler;e.addEventListener(type,handler);}
      };
      h.scan=()=>{
        if(h.loginVisible()){h.candidates=[];return [];}
        const links=Array.from(document.querySelectorAll('a[href]')).filter(a=>{
          try{const u=new URL(a.href);return /(^|\.)douyin\.com$/.test(u.hostname)&&(/^\/video\/[0-9]+/.test(u.pathname)||u.searchParams.has('modal_id')||u.searchParams.has('aweme_id'));}catch{return false;}
        });
        const images=Array.from(document.querySelectorAll('img'));
        const hoverCards=Array.from(document.querySelectorAll('div,li,article')).slice(0,4000).filter(e=>
          reactEvents(e).some(name=>/^onMouse(Enter|Leave)$/.test(name)) && (e.querySelector('img,video')||getComputedStyle(e).backgroundImage!=='none'));
        h.candidates=[];
        const add=(e,method,point=null)=>{
          if(!shown(e)||e.closest('[role="dialog"],[aria-modal="true"]'))return;
          const r=e.getBoundingClientRect();if(r.width>innerWidth*.75||r.height>innerHeight*.9)return;
          const x=point?.x??r.x+r.width/2,y=point?.y??r.y+r.height/2,hit=document.elementFromPoint(x,y);
          if(!hit||hit.closest('[role="dialog"],[aria-modal="true"]'))return;
          let owner=null,common=null;
          for(let p=e,n=0;p&&n<8;p=p.parentElement,n++){
            const b=p.getBoundingClientRect();
            if(b.width>Math.min(innerWidth*.8,r.width*1.8+60)||b.height>Math.min(innerHeight*.95,r.height*2.5+120))break;
            if(!p.contains(hit)&&!hit.contains(p))continue;
            common??=p;
            if(reactEvents(p).some(name=>/^onMouse(Enter|Leave)$/.test(name))){owner=p;break;}
          }
          owner??=common;if(!owner)return;
          if(h.candidates.some(c=>c.owner===owner||Math.abs(c.x-x)<25&&Math.abs(c.y-y)<25))return;
          h.candidates.push({element:e,owner,x,y,method});
        };
        for(const e of [...images,...links,...hoverCards])add(e,'visible-cover-with-local-hit-owner');
        h.candidates.sort((a,b)=>a.y-b.y||a.x-b.x);
        if(h.candidates.length===0){
          // This fresh guest screenshot was reviewed: first cover center (355,205).
          // Require a non-modal card-sized DOM ancestor before moving there; never click it.
          const hit=document.elementFromPoint(355,205);
          for(let e=hit,n=0;e&&n<7;e=e.parentElement,n++){
            if(e.closest('[role="dialog"],[aria-modal="true"]'))break;
            const r=e.getBoundingClientRect();
            if(r.width>=160&&r.width<innerWidth*.6&&r.height>=100&&r.height<innerHeight*.75){add(e,'reviewed-first-cover-with-dom-guard',{x:355,y:205});break;}
          }
        }
        h.candidates=h.candidates.slice(0,12);
        return h.candidates.map((c,index)=>({index,node:describe(c.element),owner:describe(c.owner),ownerReactMouseHandlers:reactEvents(c.owner),rect:rect(c.element),method:c.method}));
      };
      h.pick=index=>{
        h.scan();const candidate=h.candidates[index];if(!candidate)return null;const e=candidate.element;
        h.anchor=e;h.card=candidate.owner;
        for(const t of h.tracked)for(const type of ['mouseenter','mouseleave'])t.element.removeEventListener(type,t.handlers[type]);
        h.video=null;h.beforeVideo=null;h.beforeSource=null;h.tracked=[];h.events=[];h.movedAt=null;h.targetRect=e.getBoundingClientRect();
        for(let p=h.card,n=0;p&&n<10;p=p.parentElement,n++)h.track(p,n===0?'selected-card':'card-ancestor-'+n);
        h.track(e,'hover-target');return {x:candidate.x,y:candidate.y,card:describe(h.card),target:describe(e),rect:rect(e),method:candidate.method};
      };
      h.findVideo=()=>{
        if(!h.anchor)return null;let a=h.anchor.getBoundingClientRect();if(a.width<1||a.height<1)a=h.card.getBoundingClientRect();if(a.width<1||a.height<1)a=h.targetRect;
        const candidates=Array.from(document.querySelectorAll('video')).filter(v=>{const r=v.getBoundingClientRect();return r.width>=80&&r.height>=60&&Math.min(r.right,a.right)>Math.max(r.left,a.left)+40&&Math.min(r.bottom,a.bottom)>Math.max(r.top,a.top)+40;});
        const v=candidates.find(v=>v.currentSrc)||candidates[0]||null;
        if(v){h.video=v;h.track(v,'preview-video');for(let p=v.parentElement,n=0;p&&n<10;p=p.parentElement,n++)h.track(p,'video-ancestor-'+n);}
        return v;
      };
      h.overlay=()=>{
        const v=h.findVideo()||h.beforeVideo;if(!v)return null;const r=v.getBoundingClientRect();
        return Array.from(document.querySelectorAll('[data-toolsbox-player]')).map(host=>({host,button:host.shadowRoot?.querySelector('button')})).find(({host,button})=>{
          if(!button||getComputedStyle(host).display==='none')return false;const b=button.getBoundingClientRect();
          return b.width>0&&b.height>0&&b.left>=r.left-1&&b.left<r.right&&b.top>=r.top-1&&b.top<r.bottom;
        })||null;
      };
      h.overlayPoint=()=>{const o=h.overlay();if(!o)return null;const r=o.button.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2,disabled:o.button.disabled,insideSelectedCard:!!h.card?.contains(o.host),insideVideo:!!h.video?.contains(o.host),parent:describe(o.host.parentElement)};};
      h.exitPoint=()=>{
        const ownerRects=h.tracked.filter(t=>reactEvents(t.element).some(name=>/^onMouse(Enter|Leave)$/.test(name))).map(t=>t.element.getBoundingClientRect()).filter(r=>r.width>0&&r.width<innerWidth*.9&&r.height<innerHeight*.9);
        for(const e of [h.card,h.video])if(e?.isConnected)ownerRects.push(e.getBoundingClientRect());
        for(const [x,y] of [[2,2],[innerWidth-2,2],[2,innerHeight-2],[innerWidth-2,innerHeight-2],[innerWidth/2,2]])
          if(!ownerRects.some(r=>x>=r.left&&x<=r.right&&y>=r.top&&y<=r.bottom))return {x,y};
        return null;
      };
      h.resetEvents=()=>{h.events=[];for(const t of h.tracked){t.mouseenter=t.mouseleave=t.trustedEnter=t.trustedLeave=0;}};
      h.markBefore=()=>{h.beforeVideo=h.findVideo();h.beforeSource=h.beforeVideo?.currentSrc||'';};
      h.snapshot=phase=>{
        const v=h.findVideo(),o=h.overlay();
        const player=v?window.__toolsboxPlayers?.().players.find(p=>p.source===v.currentSrc&&p.visible):null;
        return {phase,elapsedAfterMoveMs:h.movedAt===null?null:performance.now()-h.movedAt,cardConnected:!!h.card?.isConnected,cardHovered:!!h.card?.matches(':hover'),anchorHovered:!!h.anchor?.matches(':hover'),
          video:v?{connected:v.isConnected,hovered:v.matches(':hover'),sameElement:h.beforeVideo===null?null:h.beforeVideo===v,sourceKind:!v.currentSrc?'empty':v.currentSrc.startsWith('blob:')?'blob':'http',sameSource:h.beforeSource===null?null:h.beforeSource===v.currentSrc,paused:v.paused,readyState:v.readyState,time:v.currentTime,duration:Number.isFinite(v.duration)?v.duration:null,rect:rect(v),id:player?.id,generation:player?.generation,siteId:player?.siteId}:null,
          originalVideo:h.beforeVideo?{connected:h.beforeVideo.isConnected,sourceKind:!h.beforeVideo.currentSrc?'empty':h.beforeVideo.currentSrc.startsWith('blob:')?'blob':'http',paused:h.beforeVideo.paused,time:h.beforeVideo.currentTime}:null,
          overlay:o?{disabled:o.button.disabled,insideSelectedCard:!!h.card?.contains(o.host),insideVideo:!!v?.contains(o.host),parent:describe(o.host.parentElement),rect:rect(o.button)}:null,
          ancestry:h.tracked.map(t=>({index:t.index,roles:t.roles,node:describe(t.element),reactMouseHandlers:reactEvents(t.element),connected:t.element.isConnected,hovered:t.element.matches(':hover'),mouseenter:t.mouseenter,mouseleave:t.mouseleave,trustedEnter:t.trustedEnter,trustedLeave:t.trustedLeave})),events:h.events.slice()};
      };
      window.__toolsboxHoverProbe=h;
    })();
    """;
}
