namespace ToolsBox.MediaDownloads;

public sealed partial class MediaDownloadService
{
    private static Uri? RefererOrigin(string? referer)
    {
        if (string.IsNullOrWhiteSpace(referer)) return null;
        if (!Uri.TryCreate(referer, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("来源页面必须是 HTTP(S) 地址。");
        return new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port).Uri;
    }
    private static string? SafeUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;
        if (userAgent.Length > 1024 || userAgent.Any(char.IsControl)) throw new ArgumentException("浏览器标识格式无效。");
        return userAgent;
    }
    private static void AddRequestContext(List<string> arguments, string? referer, string? userAgent)
    {
        var origin = RefererOrigin(referer);
        var agent = SafeUserAgent(userAgent);
        if (origin != null) arguments.AddRange(new[] { "--referer", origin.AbsoluteUri });
        if (agent != null) arguments.AddRange(new[] { "--user-agent", agent });
    }
}
