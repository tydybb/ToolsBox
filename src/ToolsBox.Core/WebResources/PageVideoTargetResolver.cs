namespace ToolsBox.Core.WebResources;

public sealed record PageVideoTarget(string? Url, string? Message);

public static class PageVideoTargetResolver
{
    private const string SingleDouyinVideoMessage = "当前页面无法确定单条视频。请先打开抖音单条视频详情页，或将该视频的分享链接粘贴到地址栏后再解析。";

    public static PageVideoTarget Resolve(string source)
    {
        if (!WebResourceRules.IsWebUrl(source))
            return new(null, "请先打开要解析的 HTTP(S) 视频网页。");

        var uri = new Uri(source);
        if (uri.Host is "www.bilibili.com" or "bilibili.com" && uri.AbsolutePath.StartsWith("/bangumi/play/ss", StringComparison.Ordinal))
            return new(null, "当前是 B 站番剧合集页。请先选择目标单集并播放数秒，待资源列表识别后下载，或打开该单集的 ep 详情链接。");
        if (uri.Host is not ("douyin.com" or "www.douyin.com") || uri.AbsolutePath.StartsWith("/video/", StringComparison.Ordinal))
            return new(source, null);

        string? modalId = null;
        int modalCount = 0;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string name = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            if (!string.Equals(name, "modal_id", StringComparison.Ordinal)) continue;
            modalCount++;
            modalId = separator < 0 ? "" : Uri.UnescapeDataString(pair[(separator + 1)..]);
        }

        // An explicit, unambiguous ID identifies the user's selected video without inspecting the feed.
        if (modalCount == 1 && modalId is { Length: >= 1 and <= 20 } && modalId.All(c => c is >= '0' and <= '9'))
            return new("https://www.douyin.com/video/" + modalId, null);

        if (uri.AbsolutePath is "/" or "/jingxuan" or "/jingxuan/" || modalCount > 0)
            return new(null, SingleDouyinVideoMessage);

        return new(source, null);
    }
}
