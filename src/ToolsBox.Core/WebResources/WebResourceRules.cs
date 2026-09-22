namespace ToolsBox.Core.WebResources;

public enum WebResourceKind { Image, Video, Hls, Dash }

public static class WebResourceRules
{
    public static bool IsWebUrl(string text) => text.Length <= 16384 &&
        !text.Any(char.IsControl) && Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);

    public static WebResourceKind? Classify(string url, string contentType)
    {
        if (!IsWebUrl(url)) return null;
        var extension = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        var mime = contentType.Split(';')[0].Trim().ToLowerInvariant();
        if (mime is "text/html" or "application/json" || extension is ".ts" or ".m4s" or ".cmfv" or ".cmfa" || mime == "video/mp2t") return null;
        if (extension == ".m3u8" || mime is "application/vnd.apple.mpegurl" or "application/x-mpegurl") return WebResourceKind.Hls;
        if (extension == ".mpd" || mime == "application/dash+xml") return WebResourceKind.Dash;
        if (mime.StartsWith("image/")) return WebResourceKind.Image;
        if (mime.StartsWith("video/") || extension is ".mp4" or ".webm" or ".mov" or ".mkv" or ".avi" or ".flv") return WebResourceKind.Video;
        return null;
    }
}
