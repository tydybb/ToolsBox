namespace ToolsBox.MediaDownloads;

public enum MediaDownloadError { ComponentsMissing, ParseFailed, DownloadFailed, ConversionFailed, VerificationFailed, ProtectedMedia, LoginRequired, RateLimited }
public sealed class MediaDownloadException(MediaDownloadError code) : InvalidOperationException(MessageFor(code))
{
    public MediaDownloadError Code { get; } = code;
    public static MediaDownloadException FromProcess(MediaDownloadError stage, string diagnostic)
    {
        if (diagnostic.Contains("DRM", StringComparison.OrdinalIgnoreCase)) return new(MediaDownloadError.ProtectedMedia);
        if (diagnostic.Contains("HTTP Error 429", StringComparison.OrdinalIgnoreCase)) return new(MediaDownloadError.RateLimited);
        if (diagnostic.Contains("HTTP Error 403", StringComparison.OrdinalIgnoreCase) || diagnostic.Contains("HTTP Error 401", StringComparison.OrdinalIgnoreCase) || diagnostic.Contains("sign in", StringComparison.OrdinalIgnoreCase) || diagnostic.Contains("login", StringComparison.OrdinalIgnoreCase)) return new(MediaDownloadError.LoginRequired);
        return new(stage);
    }
    private static string MessageFor(MediaDownloadError code) => code switch
    {
        MediaDownloadError.ComponentsMissing => "请先安装或导入视频组件。",
        MediaDownloadError.ParseFailed => "视频解析失败：请确认链接是支持的单个视频，并重新解析。",
        MediaDownloadError.DownloadFailed => "视频下载失败：链接可能已过期或网络中断，请重新解析后重试。",
        MediaDownloadError.ConversionFailed => "MP4 转换失败，请检查输出空间或重新导入视频组件。",
        MediaDownloadError.VerificationFailed => "输出验证失败：时长、容器或音视频轨道不完整，未发布文件。",
        MediaDownloadError.ProtectedMedia => "该媒体受 DRM 保护，无法下载。",
        MediaDownloadError.LoginRequired => "网站拒绝访问；请检查页面登录状态和下载权限后重试。",
        MediaDownloadError.RateLimited => "网站暂时限制请求频率，请稍后重试。",
        _ => "媒体处理失败。"
    };
}
