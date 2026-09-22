namespace ToolsBox.App.WebResources;

internal static class PlayerOverlayScript
{
    // A page may tamper with this DOM bridge. Only the native confirmation can authorize a download.
    public const string Install = """
    (() => {
      if (window.__toolsboxPlayers) return;
      const documentId = Array.from(crypto.getRandomValues(new Uint32Array(4)),n=>n.toString(16).padStart(8,'0')).join(''), players = new Map();
      let sequence = 0, enabled = true;
      function visibleVideo(v) {
        const r=v.getBoundingClientRect(), style=getComputedStyle(v);
        const x=Math.max(0,r.left), y=Math.max(0,r.top), right=Math.min(innerWidth,r.right), bottom=Math.min(innerHeight,r.bottom);
        const hit=right>x && bottom>y ? document.elementFromPoint((x+right)/2,(y+bottom)/2) : null;
        const container=v.closest('[data-e2e="feed-active-video"],.bpx-player-video-area') || v.parentElement;
        return r.width>=80 && r.height>=60 && right-x>=50 && bottom-y>=40 &&
          style.visibility==='visible' && style.display!=='none' && Number(style.opacity)>0 &&
          !!hit && (hit===v || hit.contains(v) || !!container?.contains(hit));
      }
      function bilibiliEpisodeId(v) {
        const page=/^\/bangumi\/play\/(ss|ep)([1-9][0-9]{0,19})\/?$/.exec(location.pathname);
        if (!page || !v.closest('.bpx-player-video-area')?.closest('.bpx-player-primary-area')) return '';
        const videos=Array.from(document.querySelectorAll('video'));
        if (videos.length>32 || videos.filter(visibleVideo).length!==1 || !visibleVideo(v)) return '';
        const active=document.querySelectorAll('a[class*="EpisodeVirtualList_activeNumber__"][href]');
        if (active.length!==1) return '';
        try {
          const u=new URL(active[0].href), match=/^\/bangumi\/play\/ep([1-9][0-9]{0,19})\/?$/.exec(u.pathname);
          if (!match || u.origin!==location.origin || (page[1]==='ep' && page[2]!==match[1])) return '';
          return match[1];
        } catch {return '';}
      }
      function siteId(v) {
        if (['www.bilibili.com','bilibili.com'].includes(location.hostname)) return bilibiliEpisodeId(v);
        if (!['www.douyin.com','douyin.com'].includes(location.hostname)) return '';
        const ids = new Set();
        for (let e=v.parentElement, n=0; e && n<9; e=e.parentElement,n++) {
          if (e.querySelectorAll('video').length !== 1) break;
          for (const token of e.classList) { const m=/^video_([0-9]{1,20})$/.exec(token); if(m) ids.add(m[1]); }
          for (const a of Array.from(e.querySelectorAll('a[href*="aweme_id="]')).slice(0,40)) {
            try { const u=new URL(a.href), id=u.searchParams.get('aweme_id');
              if (['www.douyin.com','douyin.com'].includes(u.hostname) && /^[0-9]{1,20}$/.test(id||'')) ids.add(id);
            } catch {}
          }
        }
        return ids.size===1 ? [...ids][0] : '';
      }
      function availability(s,ready,label='待识别') {
        s.ready=ready; s.button.disabled=!ready;
        s.button.textContent=ready ? '↓ 下载此视频' : label;
        s.button.setAttribute('aria-label','宝哥工具箱：'+(ready ? '下载此视频' : label));
        s.button.style.cursor=ready ? 'pointer' : 'default';
        s.button.style.background=ready ? '#1769d2' : '#525966';
      }
      function placeOverlay(v,s,visible) {
        // Keep the button inside the original hover ancestors. A document-level overlay
        // causes a real mouseleave on preview cards as soon as the pointer reaches it.
        if (s.host.parentElement!==v.parentElement) v.parentElement.append(s.host);
        // The axis-aligned origin measurement supports translation and positive scaling.
        // For rotated/skewed/mirrored/3D cards keep the resource-list entry, not a misplaced button.
        for (let e=v.parentElement; visible && e; e=e.parentElement) {
          const style=getComputedStyle(e);
          if (style.perspective!=='none' || style.rotate!=='none' ||
              (style.scale!=='none' && style.scale.split(/\s+/).some(n=>!(Number(n)>0)))) visible=false;
          if (style.transform!=='none') {
            const m=new DOMMatrixReadOnly(style.transform);
            if (!m.is2D || m.a<=0 || m.d<=0 || Math.abs(m.b)>0.000001 || Math.abs(m.c)>0.000001) visible=false;
          }
        }
        s.host.style.display=visible ? 'block' : 'none';
        if (!visible) return;
        // Measure the local origin and scale instead of assuming viewport coordinates:
        // positioned, scrolled and transformed card ancestors create containing blocks.
        s.host.style.left='0px'; s.host.style.top='0px';
        const origin=s.host.getBoundingClientRect(), video=v.getBoundingClientRect();
        if (!(origin.width>0 && origin.height>0)) {s.host.style.display='none';return;}
        s.host.style.left=((Math.max(0,video.left)+8-origin.left)/origin.width)+'px';
        s.host.style.top=((Math.max(0,video.top)+8-origin.top)/origin.height)+'px';
      }
      function refresh() {
        for (const [v,s] of players) if (!v.isConnected) { s.host.remove(); players.delete(v); }
        for (const v of Array.from(document.querySelectorAll('video')).slice(0,32)) if (!players.has(v)) {
          const host=document.createElement('div'); host.setAttribute('data-toolsbox-player','');
          host.style.cssText='all:initial;position:absolute!important;width:1px!important;height:1px!important;z-index:2147483647!important;pointer-events:none!important;';
          const shadow=host.attachShadow({mode:'open'}), button=document.createElement('button');
          button.type='button';
          button.style.cssText='all:initial;display:block;width:max-content;white-space:nowrap;cursor:pointer;pointer-events:auto;background:#1769d2;color:white;font:600 13px/20px sans-serif;padding:7px 11px;border:1px solid #ffffff66;border-radius:7px;box-shadow:0 2px 8px #0008;';
          shadow.append(button);
          const s={id:'p'+(++sequence),generation:0,key:'',encrypted:false,ready:false,readyUrl:'',host,button}; players.set(v,s);
          availability(s,false,'请先播放');
          v.addEventListener('encrypted',()=>{s.encrypted=true; availability(s,false);});
          v.addEventListener('emptied',()=>{s.generation++; s.encrypted=false; availability(s,false,'请先播放');});
          v.addEventListener('loadstart',()=>availability(s,false,'请先播放'));
          // Only isolate button presses; genuine hover entry/exit must still reach the site.
          for (const name of ['pointerdown','pointerup','mousedown','mouseup','dblclick'])
            button.addEventListener(name,e=>e.stopPropagation());
          button.addEventListener('click',e=>{
            e.preventDefault(); e.stopPropagation(); if (!e.isTrusted || !enabled) return;
            refresh(); if (!s.ready || button.disabled) return;
            window.chrome?.webview?.postMessage({type:'toolsbox-player',document:documentId,id:s.id,generation:s.generation});
          });
        }
        const result=[];
        for (const [v,s] of players) {
          const encrypted=s.encrypted || !!v.mediaKeys;
          const source=(v.currentSrc||'').slice(0,16384), id=siteId(v), key=source+'\n'+id+'\n'+encrypted+'\n'+!!v.srcObject;
          if (key!==s.key) {s.key=key;s.generation++; availability(s,false);}
          const fs=document.fullscreenElement, visible=enabled && visibleVideo(v);
          const playable=!!source && Number.isFinite(v.duration) && v.duration>0;
          if (!visible || !playable || encrypted || v.srcObject || s.readyUrl!==location.href) availability(s,false,playable ? '待识别' : '请先播放');
          placeOverlay(v,s,visible && !v.srcObject && !encrypted && fs!==v);
          result.push({id:s.id,generation:s.generation,source,siteId:id,visible,encrypted,
            streamObject:!!v.srcObject,duration:Number.isFinite(v.duration)?v.duration:null});
        }
        return {document:documentId,url:location.href,title:document.title.slice(0,300),players:result};
      }
      window.__toolsboxPlayers = value => { if(typeof value==='boolean') enabled=value; return refresh(); };
      // This bridge controls presentation only. The native app revalidates every confirmed download.
      window.__toolsboxPlayerAvailability = value => {
        const current=refresh();
        if (!value || value.document!==documentId || value.url!==location.href || !Array.isArray(value.players)) return;
        for (const [v,s] of players) {
          const player=current.players.find(p=>p.id===s.id), match=value.players.find(p=>p.id===s.id);
          const ready=enabled && !!player && player.visible && player.duration>0 && Number.isFinite(player.duration) && !player.encrypted && !player.streamObject &&
            !!match && match.ready===true && match.generation===player.generation && match.source===player.source && match.siteId===player.siteId;
          s.readyUrl=ready ? location.href : '';
          availability(s,ready,player?.source && player.duration>0 ? '待识别' : '请先播放');
        }
      };
      const observer=new MutationObserver(()=>{try{refresh();}catch{}});
      observer.observe(document,{subtree:true,childList:true,attributes:true,attributeFilter:['src','class','href','data-e2e']});
      const timer=setInterval(()=>{try{refresh();}catch{}},400);
      addEventListener('pagehide',()=>{clearInterval(timer);observer.disconnect();},{once:true});
    })();
    """;
}
