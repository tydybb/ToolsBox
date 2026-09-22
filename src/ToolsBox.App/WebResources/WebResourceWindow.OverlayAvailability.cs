using System.Text.Json;
using ToolsBox.Core.WebResources;

namespace ToolsBox.App.WebResources;

public partial class WebResourceWindow
{
    private async Task UpdateOverlayAvailability(int frameId, PlayerDocument document)
    {
        if (_closing || _closed || Browser.CoreWebView2 == null || _navigatingFrames.Contains(frameId)) return;
        var state = new
        {
            document = document.Document,
            url = document.Url,
            players = document.Players.Select(player => new
            {
                id = player.Id,
                generation = player.Generation,
                source = player.Source,
                siteId = player.SiteId,
                ready = CaptureEnabled.IsChecked == true &&
                    PlayerMediaBinding.Resolve(document, player, _playerObservedMedia, _douyinMedia) != null
            }).ToArray()
        };
        // Scripts can be delayed until after a document or player change. The bridge must
        // match this exact snapshot before enabling UI; it never authorizes a download.
        string script = "window.__toolsboxPlayerAvailability?.(" + JsonSerializer.Serialize(state) + ")";
        try
        {
            if (frameId == 0) await Browser.CoreWebView2.ExecuteScriptAsync(script);
            else if (_playerFrames.TryGetValue(frameId, out var frame)) await frame.ExecuteScriptAsync(script);
        }
        catch { /* Navigation and destroyed frames leave the overlay unavailable until another poll. */ }
    }
}
