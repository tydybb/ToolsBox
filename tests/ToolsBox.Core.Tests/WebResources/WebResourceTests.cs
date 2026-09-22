using System.Reflection;

namespace ToolsBox.Core.Tests.WebResources;

public class WebResourceTests
{
    private static Type Rules => typeof(ToolsBox.Core.WorkCountdown.WorkSchedule).Assembly.GetType("ToolsBox.Core.WebResources.WebResourceRules")
        ?? throw new Xunit.Sdk.XunitException("Missing WebResourceRules");

    [Theory]
    [InlineData("https://a.test/movie.m3u8?token=1", "application/octet-stream", "Hls")]
    [InlineData("https://a.test/movie.mpd", "application/dash+xml", "Dash")]
    [InlineData("https://a.test/photo", "image/jpeg", "Image")]
    [InlineData("https://a.test/movie.mp4", "video/mp4", "Video")]
    [InlineData("https://a.test/segment.ts", "video/mp2t", null)]
    [InlineData("https://a.test/segment.m4s", "video/mp4", null)]
    [InlineData("blob:https://a.test/123", "video/mp4", null)]
    [InlineData("https://user:pass@a.test/a.mp4", "video/mp4", null)]
    [InlineData("https://a.test/a.mp4", "text/html", null)]
    public void ClassifiesOnlyUsableResources(string url, string mime, string? expected)
        => Assert.Equal(expected, Rules.GetMethod("Classify")!.Invoke(null, [url, mime])?.ToString());

    [Theory]
    [InlineData("https://a.test/x?sig=a%2Fb", true)]
    [InlineData("file:///C:/secret", false)]
    [InlineData("https://u:p@a.test/x", false)]
    public void ResourceUrlsAllowSignedHttpButRejectLocalFilesAndCredentials(string url, bool expected)
        => Assert.Equal(expected, Rules.GetMethod("IsWebUrl")!.Invoke(null, [url]));
}
