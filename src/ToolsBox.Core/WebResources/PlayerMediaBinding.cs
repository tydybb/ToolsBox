using System.Text.RegularExpressions;

namespace ToolsBox.Core.WebResources;

public sealed record PlayerSnapshot
{
    public string Id { get; init; } = "";
    public int Generation { get; init; }
    public string Source { get; init; } = "";
    public string SiteId { get; init; } = "";
    public bool Visible { get; init; }
    public bool Encrypted { get; init; }
    public bool StreamObject { get; init; }
    public double? Duration { get; init; }
}

public sealed record PlayerDocument
{
    public string Document { get; init; } = "";
    public string Url { get; init; } = "";
    public string Title { get; init; } = "";
    public PlayerSnapshot[] Players { get; init; } = [];
}

public sealed record PlayerMediaChoice(string Url, string Label, WebResourceKind Kind, bool RequiresPageExtraction = false);
public sealed record BoundPlayerMedia(string Title, double? Duration, IReadOnlyList<PlayerMediaChoice> Choices);

public static class PlayerMediaBinding
{
    public static BoundPlayerMedia? Resolve(PlayerDocument document, PlayerSnapshot player,
        IReadOnlyDictionary<string, WebResourceKind> observedMedia,
        IReadOnlyDictionary<string, DouyinMediaItem> douyinMedia)
    {
        if (document.Url == null || !WebResourceRules.IsWebUrl(document.Url) || !player.Visible || player.Encrypted || player.StreamObject ||
            string.IsNullOrEmpty(player.Source) || player.SiteId == null || player.Duration is not > 0 ||
            !double.IsFinite(player.Duration.Value) || player.Duration.Value > TimeSpan.MaxValue.TotalSeconds / 2) return null;
        if (WebResourceRules.IsWebUrl(player.Source) && observedMedia.TryGetValue(player.Source, out var kind) && kind != WebResourceKind.Image)
            return new(document.Title, player.Duration, [new(player.Source, "当前播放来源", kind)]);
        if (new Uri(document.Url).Host is "www.bilibili.com" or "bilibili.com")
            return ResolveBilibili(document, player);
        if (!player.Source.StartsWith("blob:", StringComparison.Ordinal) ||
            new Uri(document.Url).Host is not ("www.douyin.com" or "douyin.com") ||
            !douyinMedia.TryGetValue(player.SiteId, out var item) || item.Id != player.SiteId ||
            item.DurationSeconds is not > 0 || Math.Abs(item.DurationSeconds.Value - player.Duration.Value) > Math.Max(2, player.Duration.Value * 0.02)) return null;
        var choices = item.Variants.Where(v => WebResourceRules.IsWebUrl(v.Url))
            .Select(v => new PlayerMediaChoice(v.Url, v.Label, WebResourceKind.Video)).ToArray();
        return choices.Length == 0 ? null : new(item.Title, item.DurationSeconds, choices);
    }

    private static BoundPlayerMedia? ResolveBilibili(PlayerDocument document, PlayerSnapshot player)
    {
        var page = new Uri(document.Url);
        // Inspect the original path so URI normalization cannot turn an encoded or malformed ID into a match.
        var path = Regex.Match(document.Url, @"\A(?i:https?)://[^/?#]+/bangumi/play/(ss|ep)([1-9][0-9]{0,19})/?(?:[?#].*)?\z", RegexOptions.CultureInvariant);
        if (!path.Success || !string.IsNullOrEmpty(page.UserInfo) || !page.IsDefaultPort ||
            !Regex.IsMatch(player.SiteId, @"\A[1-9][0-9]{0,19}\z", RegexOptions.CultureInvariant) ||
            (path.Groups[1].Value == "ep" && path.Groups[2].Value != player.SiteId) ||
            !player.Source.StartsWith("blob:", StringComparison.Ordinal) ||
            !Uri.TryCreate(player.Source[5..], UriKind.Absolute, out var blobOrigin) ||
            !string.Equals(blobOrigin.GetLeftPart(UriPartial.Authority), page.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase) ||
            document.Players == null || document.Players.Count(p => p is { Visible: true }) != 1 ||
            !document.Players.Any(p => p == player)) return null;

        // This is an identified episode page, not a captured progressive media file.
        return new(document.Title, player.Duration,
            [new("https://www.bilibili.com/bangumi/play/ep" + player.SiteId,
                "当前单集·下载时解析并合并音视频", WebResourceKind.Video, RequiresPageExtraction: true)]);
    }

    public static bool IsSameTarget(PlayerDocument before, PlayerSnapshot player, PlayerDocument after) =>
        !string.IsNullOrEmpty(before.Document) && after.Players != null && before.Document == after.Document && before.Url == after.Url &&
        after.Players.Count(p => p.Id == player.Id) == 1 &&
        after.Players.Any(p => p.Id == player.Id && p.Generation == player.Generation && p.Source == player.Source &&
            p.SiteId == player.SiteId && p.Visible && !p.Encrypted && !p.StreamObject);
}
