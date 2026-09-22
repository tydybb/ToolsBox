using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using ToolsBox.Core.WebResources;

namespace ToolsBox.App.WebResources;

public partial class WebResourceWindow
{
    private long _playerDocumentGeneration;
    // Unlike list/confirmation generations, this survives same-document SPA URL changes.
    private long _playerEvidenceDocumentGeneration;
    private int _nextFrameId, _metadataReads;
    private bool _pollingPlayers, _confirmingPlayer;
    private DateTime _lastPlayerMessage;
    // Safe diagnostic for integration probes: never store the response URL, body, or exception message.
    private string _lastPlayerMetadataResult = "not-observed";
    private DispatcherTimer? _playerTimer;
    private readonly Dictionary<int, CoreWebView2Frame> _playerFrames = [];
    private readonly Dictionary<int, long> _frameGenerations = [];
    private readonly Dictionary<int, string> _frameSources = [];
    private readonly HashSet<int> _navigatingFrames = [];
    private readonly Dictionary<int, PlayerDocument> _playerDocuments = [];
    private readonly Dictionary<string, WebResourceKind> _playerObservedMedia = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DouyinMediaItem> _douyinMedia = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions PlayerJson = new() { PropertyNameCaseInsensitive = true, MaxDepth = 8 };

    private async Task InitializePlayers(CoreWebView2 core)
    {
        core.Settings.IsWebMessageEnabled = true;
        core.WebMessageReceived += (_, e) => PlayerMessage(0, e);
        core.FrameCreated += (_, e) => AddPlayerFrame(e.Frame);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(PlayerOverlayScript.Install);
        _playerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _playerTimer.Tick += async (_, _) => await PollPlayers();
        _playerTimer.Start();
    }

    private void AddPlayerFrame(CoreWebView2Frame frame)
    {
        if (_closing || _playerFrames.Count >= 16) return;
        int id = ++_nextFrameId; _playerFrames[id] = frame; _frameGenerations[id] = 0;
        frame.WebMessageReceived += (_, e) => PlayerMessage(id, e);
        frame.FrameCreated += (_, e) => AddPlayerFrame(e.Frame);
        frame.NavigationStarting += (_, e) =>
        {
            if (_closing || !_frameGenerations.TryGetValue(id, out long epoch)) return;
            _playerDocuments.Remove(id); _frameGenerations[id] = epoch + 1; _frameSources[id] = e.Uri; _navigatingFrames.Add(id);
        };
        frame.NavigationCompleted += (_, _) => _navigatingFrames.Remove(id);
        frame.Destroyed += (_, _) =>
        { _playerDocuments.Remove(id); _playerFrames.Remove(id); _frameGenerations.Remove(id); _frameSources.Remove(id); _navigatingFrames.Remove(id); };
    }

    private void ResetPlayersForNavigation()
    {
        _playerEvidenceDocumentGeneration++;
        ResetResourcesForPageChange();
        // Cached <video> media may not raise another response on Reload/Back. Keep only
        // bounded URL/type evidence; a visible player in the new document must rebind it.
        // This never republishes old list rows or carries site-specific metadata across documents.
        _douyinMedia.Clear();
    }

    private void ResetResourcesForPageChange()
    {
        _playerDocumentGeneration++;
        _clearedPlayerResources.Clear();
        _playerDocuments.Clear();
        _resources.Clear();
        ResourcesGrid.SelectedItem = null;
    }

    private async Task<PlayerDocument?> ReadPlayerDocument(int frameId)
    {
        if (_closing || _closed || Browser.CoreWebView2 == null) return null;
        if (_navigatingFrames.Contains(frameId)) return null;
        long epoch = _playerDocumentGeneration, frameEpoch = _frameGenerations.GetValueOrDefault(frameId);
        string script = "({actualUrl:location.href,snapshot:window.__toolsboxPlayers?.(" + (CaptureEnabled.IsChecked == true ? "true" : "false") + ") ?? null})";
        try
        {
            string json = frameId == 0 ? await Browser.CoreWebView2.ExecuteScriptAsync(script) :
                _playerFrames.TryGetValue(frameId, out var frame) ? await frame.ExecuteScriptAsync(script) : "null";
            if (json.Length > 256 * 1024) return null;
            using var envelope = JsonDocument.Parse(json);
            string? actualUrl = envelope.RootElement.GetProperty("actualUrl").GetString();
            var doc = envelope.RootElement.GetProperty("snapshot").Deserialize<PlayerDocument>(PlayerJson);
            // Nullable annotations do not validate JSON sent by an untrusted document.
            if (doc is null || doc.Document is not { Length: > 0 and <= 64 } || doc.Url == null ||
                !WebResourceRules.IsWebUrl(doc.Url) || doc.Title is not { Length: <= 300 } ||
                doc.Players is not { Length: <= 32 } || doc.Players.Any(p => p is null || p.Id is not { Length: > 0 and <= 64 } ||
                    p.Source is not { Length: <= 16384 } || p.SiteId is not { Length: <= 20 } || p.Generation < 0)) return null;
            if (doc.Players.Select(p => p.Id).Distinct().Count() != doc.Players.Length) return null;
            if (epoch != _playerDocumentGeneration || frameEpoch != _frameGenerations.GetValueOrDefault(frameId) || _navigatingFrames.Contains(frameId) || doc.Url != actualUrl) return null;
            if (frameId == 0 && doc.Url != Browser.CoreWebView2.Source) return null;
            if (frameId != 0 && (!_frameSources.TryGetValue(frameId, out var source) || !WebResourceRules.IsWebUrl(source) ||
                new Uri(source).GetLeftPart(UriPartial.Authority) != new Uri(doc.Url).GetLeftPart(UriPartial.Authority))) return null;
            return doc;
        }
        catch { return null; }
    }

    private async Task PollPlayers()
    {
        if (_pollingPlayers || _closed || _closing) return;
        _pollingPlayers = true; long epoch = _playerDocumentGeneration;
        try
        {
            foreach (int frame in new[] { 0 }.Concat(_playerFrames.Keys.ToArray()))
            {
                var document = await ReadPlayerDocument(frame);
                if (epoch != _playerDocumentGeneration || _closing) return;
                if (document != null)
                {
                    _playerDocuments[frame] = document;
                    PublishPlayerResources(frame, document);
                    await UpdateOverlayAvailability(frame, document);
                }
                else _playerDocuments.Remove(frame);
            }
        }
        finally { _pollingPlayers = false; }
    }

    private void PlayerMessage(int frame, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_closing || _closed || CaptureEnabled.IsChecked != true || _confirmingPlayer ||
            DateTime.UtcNow - _lastPlayerMessage < TimeSpan.FromSeconds(2)) return;
        try
        {
            var json = args.WebMessageAsJson;
            if (json.Length > 512) return;
            using var message = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var value = message.RootElement;
            if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 4 ||
                value.GetProperty("type").GetString() != "toolsbox-player") return;
            string? doc = value.GetProperty("document").GetString(), id = value.GetProperty("id").GetString();
            int generation = value.GetProperty("generation").GetInt32();
            if (!_playerDocuments.TryGetValue(frame, out var snapshot))
            { _lastPlayerMessage = DateTime.UtcNow; Status.Text = "正在识别播放器，请稍后再点一次下载。"; return; }
            if (snapshot.Url != args.Source || snapshot.Document != doc ||
                string.IsNullOrEmpty(id) || id.Length > 64 || generation < 0) return;
            _lastPlayerMessage = DateTime.UtcNow;
            // Never run a modal loop inside a WebView2 callback. Messages are selection hints, not authorization.
            _ = Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await ConfirmPlayerDownload(frame, doc, id, generation); }
                catch { if (!_closing) Status.Text = "播放器已变化，请重新点击下载。"; }
            }));
        }
        catch { /* Malformed / forged page messages do not cause any native operation. */ }
    }

    private async Task<bool> ConfirmPlayerDownload(int? selectedFrame = null, string? selectedDocument = null, string? selectedId = null, int? selectedGeneration = null)
    {
        if (_confirmingPlayer || _closing || _closed) return true;
        if (CaptureEnabled.IsChecked != true) { Status.Text = "请先启用资源识别，再播放目标视频。"; return true; }
        _confirmingPlayer = true;
        long epoch = _playerDocumentGeneration;
        string topSource = Browser.CoreWebView2?.Source ?? "";
        try
        {
            var candidates = new List<PlayerDownloadCandidate>();
            int[] frames = selectedFrame is { } f ? [f] : [0, .. _playerFrames.Keys];
            foreach (int frame in frames)
            {
                var doc = await ReadPlayerDocument(frame);
                if (doc == null || (selectedDocument != null && doc.Document != selectedDocument)) continue;
                foreach (var player in doc.Players.Where(p => p.Visible && (selectedId == null || p.Id == selectedId && p.Generation == selectedGeneration)))
                {
                    var bound = PlayerMediaBinding.Resolve(doc, player, _playerObservedMedia, _douyinMedia);
                    if (bound != null) candidates.Add(new(frame, _frameGenerations.GetValueOrDefault(frame), doc, player, bound));
                }
            }
            if (_closing || epoch != _playerDocumentGeneration || topSource != Browser.CoreWebView2?.Source) return true;
            if (candidates.Count == 0)
            {
                if (selectedId == null && (!Uri.TryCreate(topSource, UriKind.Absolute, out var page) || page.Host is not ("www.douyin.com" or "douyin.com"))) return false;
                if (selectedId == null && PageVideoTargetResolver.Resolve(topSource).Url != null)
                { Status.Text = "未绑定到播放器媒体，正在尝试单视频链接解析…"; return false; }
                Status.Text = "已检查播放器，但尚未找到能准确对应的完整媒体。请打开单条视频并播放数秒，再点播放器下载；直播、DRM 或无法绑定的 blob 不支持。";
                return true;
            }
            var dialog = new PlayerDownloadDialog(candidates, OutputFolder.Text) { Owner = _browserPopup ?? (Window)this };
            if (dialog.ShowDialog() != true) { Status.Text = "已取消下载。"; return true; }
            var chosen = dialog.Candidate;
            var current = await ReadPlayerDocument(chosen.FrameId);
            if (_closing || CaptureEnabled.IsChecked != true || epoch != _playerDocumentGeneration || topSource != Browser.CoreWebView2?.Source ||
                chosen.FrameGeneration != _frameGenerations.GetValueOrDefault(chosen.FrameId) || _navigatingFrames.Contains(chosen.FrameId) ||
                current == null || !PlayerMediaBinding.IsSameTarget(chosen.Document, chosen.Player, current))
            { Status.Text = "页面或播放器已经切换，本次下载未开始。请在新视频上重新确认。"; return true; }
            var rebound = PlayerMediaBinding.Resolve(current, current.Players.Single(p => p.Id == chosen.Player.Id), _playerObservedMedia, _douyinMedia);
            if (rebound == null || !rebound.Choices.Contains(dialog.Choice))
            { Status.Text = "媒体来源已失效，请重新识别后下载。"; return true; }
            var choice = dialog.Choice;
            var row = PublishPlayerResource(chosen, choice, explicitlySelected: true);
            if (row == null) { Status.Text = "已达到资源上限，请清空列表后重试。"; return true; }
            OutputFolder.Text = dialog.Folder;
            StartDownload(new WebDownloadRow(row, dialog.Folder));
            Status.Text = "已确认播放器来源并加入下载队列，将校验最终 MP4。";
            return true;
        }
        finally { _confirmingPlayer = false; }
    }

    private async Task ObservePlayerMetadata(CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (!DouyinMediaParser.IsMetadataResponse(e.Request.Uri) || _metadataReads >= 4 ||
            !Uri.TryCreate(_page, UriKind.Absolute, out var page) || page.Host is not ("www.douyin.com" or "douyin.com")) return;
        if (e.Response.Headers.Contains("Content-Length") && long.TryParse(e.Response.Headers.GetHeader("Content-Length"), out var length) && length > 4 * 1024 * 1024) return;
        long epoch = _playerEvidenceDocumentGeneration; _metadataReads++;
        _lastPlayerMetadataResult = "reading";
        try
        {
            using var content = await e.Response.GetContentAsync().WaitAsync(TimeSpan.FromSeconds(8), _lifetime.Token);
            if (content == null) { _lastPlayerMetadataResult = "empty-stream"; return; }
            using var buffer = new MemoryStream(); byte[] block = new byte[16384]; int count;
            while ((count = await content.ReadAsync(block, _lifetime.Token)) > 0)
            { if (buffer.Length + count > 4 * 1024 * 1024) return; buffer.Write(block, 0, count); }
            if (_closing || epoch != _playerEvidenceDocumentGeneration || CaptureEnabled.IsChecked != true)
            { _lastPlayerMetadataResult = "stale-document"; return; }
            var parsed = DouyinMediaParser.Parse(Encoding.UTF8.GetString(buffer.ToArray()));
            _lastPlayerMetadataResult = "parsed=" + parsed.Count;
            foreach (var item in parsed)
            {
                if (_douyinMedia.Count >= 100 && !_douyinMedia.ContainsKey(item.Id)) _douyinMedia.Remove(_douyinMedia.Keys.First());
                _douyinMedia[item.Id] = item;
            }
        }
        catch (Exception error) { _lastPlayerMetadataResult = error.GetType().Name; }
        finally { _metadataReads--; }
    }
}
