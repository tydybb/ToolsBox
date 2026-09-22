using ToolsBox.Core.WebResources;

namespace ToolsBox.App.WebResources;

internal sealed record PlayerResourceBinding(long PageGeneration, string TopSource,
    PlayerDownloadCandidate Candidate, PlayerMediaChoice Choice);

public partial class WebResourceWindow
{
    private sealed record PlayerResourceKey(long PageGeneration, int Frame, long FrameGeneration,
        string Document, string PageUrl, string Player, int Generation, string Source, string SiteId, string Url);

    private readonly HashSet<PlayerResourceKey> _clearedPlayerResources = [];
    private const string StalePlayerResourceMessage = "页面或播放器已切换，该资源未开始下载。请重新识别当前视频。";

    // This path only publishes already observed metadata. It never reads cookies or starts downloads.
    private void PublishPlayerResources(int frame, PlayerDocument document)
    {
        if (_closed || _closing || CaptureEnabled.IsChecked != true || _navigatingFrames.Contains(frame)) return;
        foreach (var player in document.Players)
        {
            var media = PlayerMediaBinding.Resolve(document, player, _playerObservedMedia, _douyinMedia);
            if (media == null) continue;
            var candidate = new PlayerDownloadCandidate(frame, _frameGenerations.GetValueOrDefault(frame), document, player, media);
            foreach (var choice in media.Choices) PublishPlayerResource(candidate, choice);
        }
    }

    private static PlayerResourceKey ResourceKey(PlayerResourceBinding binding)
    {
        var c = binding.Candidate;
        return new(binding.PageGeneration, c.FrameId, c.FrameGeneration, c.Document.Document, c.Document.Url,
            c.Player.Id, c.Player.Generation, c.Player.Source, c.Player.SiteId, binding.Choice.Url);
    }

    // Network responses carry no player identity. Keep their raw rows suppressed for this navigation,
    // while a newly identified player can still publish a fully validated binding for the same URL.
    private bool IsClearedPlayerMediaUrl(string url) => _clearedPlayerResources.Any(key =>
        key.PageGeneration == _playerDocumentGeneration && key.Url == url);

    private WebResourceRow? PublishPlayerResource(PlayerDownloadCandidate candidate, PlayerMediaChoice choice, bool explicitlySelected = false)
    {
        var binding = new PlayerResourceBinding(_playerDocumentGeneration, Browser.CoreWebView2?.Source ?? _page, candidate, choice);
        var key = ResourceKey(binding);
        if (explicitlySelected) _clearedPlayerResources.RemoveWhere(cleared =>
            cleared.PageGeneration == key.PageGeneration && cleared.Url == key.Url);
        else if (_clearedPlayerResources.Contains(key)) return null;

        var existing = _resources.FirstOrDefault(r => r.IsVideo && r.Url == choice.Url && r.PageUrl == candidate.Document.Url);
        if (existing?.PlayerBinding is { } previous && ResourceKey(previous) == key && previous.Choice == choice) return existing;
        if (existing == null && _resources.Count >= 2000) return null;

        // Replace at the same index: preserve list order and raw-row selection, but never mutate queued targets.
        var row = new WebResourceRow(choice.Url, candidate.Document.Url, choice.Kind, existing?.Size)
        {
            Name = candidate.Media.Title,
            CapturedVideo = choice.Kind == WebResourceKind.Video && !choice.RequiresPageExtraction,
            ExpectedDuration = candidate.Media.Duration,
            PlayerBinding = binding,
            IsSelected = existing is { PlayerBinding: null, IsSelected: true },
            Thumbnail = existing?.Thumbnail
        };
        row.Formats.Clear(); row.Formats.Add(new(null, choice.Label)); row.SelectedFormat = row.Formats[0];
        if (existing == null) _resources.Add(row);
        else
        {
            bool focused = ReferenceEquals(ResourcesGrid.SelectedItem, existing);
            _resources[_resources.IndexOf(existing)] = row;
            if (focused) ResourcesGrid.SelectedItem = row;
        }
        return row;
    }

    private async Task<bool> ValidatePlayerResource(WebResourceRow row)
    {
        if (row.PlayerBinding is not { } binding) return true;
        bool Current() => !_closing && !_closed && CaptureEnabled.IsChecked == true &&
            binding.PageGeneration == _playerDocumentGeneration && binding.TopSource == Browser.CoreWebView2?.Source &&
            binding.Candidate.FrameGeneration == _frameGenerations.GetValueOrDefault(binding.Candidate.FrameId) &&
            !_navigatingFrames.Contains(binding.Candidate.FrameId);
        if (!Current()) return false;
        var candidate = binding.Candidate;
        var document = await ReadPlayerDocument(candidate.FrameId);
        if (!Current() || document == null || !PlayerMediaBinding.IsSameTarget(candidate.Document, candidate.Player, document)) return false;
        var media = PlayerMediaBinding.Resolve(document, document.Players.Single(p => p.Id == candidate.Player.Id), _playerObservedMedia, _douyinMedia);
        return media != null && media.Choices.Contains(binding.Choice);
    }

    private void ClearPlayerResources()
    {
        foreach (var row in _resources)
            if (row.PlayerBinding is { } binding && _clearedPlayerResources.Count < 2000) _clearedPlayerResources.Add(ResourceKey(binding));
        _resources.Clear();
    }
}
