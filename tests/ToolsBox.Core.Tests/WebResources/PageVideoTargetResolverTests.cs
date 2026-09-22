using ToolsBox.Core.WebResources;

namespace ToolsBox.Core.Tests.WebResources;

public class PageVideoTargetResolverTests
{
    private static PageVideoTarget Resolve(string source) => PageVideoTargetResolver.Resolve(source);

    [Theory]
    [InlineData("https://www.bilibili.com/bangumi/play/ss33415?from_spmid=666.4.hotlist.0")]
    [InlineData("https://bilibili.com/bangumi/play/ss33415/")]
    public void BilibiliSeasonWithoutBoundPlayerNeverFallsBackToWholeSeason(string source)
    {
        var result = Resolve(source);
        Assert.Null(result.Url);
        Assert.Contains("单集", result.Message);
    }

    [Theory]
    [InlineData("https://www.douyin.com/")]
    [InlineData("https://www.douyin.com/jingxuan")]
    [InlineData("https://www.douyin.com/jingxuan/?recommend=1")]
    [InlineData("https://douyin.com")]
    [InlineData("http://DOUYIN.com/?recommend=1")]
    [InlineData("https://www.douyin.com/?recommend=1#feed")]
    [InlineData("https://www.douyin.com/?modal_id=")]
    [InlineData("https://www.douyin.com/?modal_id=abc")]
    [InlineData("https://www.douyin.com/?modal_id=１２３")]
    [InlineData("https://www.douyin.com/?modal_id=١٢٣")]
    [InlineData("https://www.douyin.com/?modal_id=+123")]
    [InlineData("https://www.douyin.com/?modal_id=-123")]
    [InlineData("https://www.douyin.com/?modal_id=123%0A")]
    [InlineData("https://www.douyin.com/?modal_id=123%2F456")]
    [InlineData("https://www.douyin.com/?modal_id=123456789012345678901")]
    [InlineData("https://www.douyin.com/?modal_id=123&modal_id=456")]
    [InlineData("https://www.douyin.com/?modal_id=123&modal_id=123")]
    [InlineData("https://www.douyin.com/?modal_id=123&%6Dodal_id=456")]
    public void HomepageOrAmbiguousModalAsksForOneVideo(string source)
    {
        var result = Resolve(source);
        Assert.Null(result.Url);
        Assert.Contains("单条视频", result.Message);
        Assert.Contains("分享链接", result.Message);
        Assert.DoesNotContain("http", result.Message);
    }

    [Theory]
    [InlineData("https://www.douyin.com/?modal_id=7123456789012345678", "7123456789012345678")]
    [InlineData("https://douyin.com/?recommend=1&modal_id=123&other=value", "123")]
    [InlineData("https://www.douyin.com/user/example?modal_id=12345678901234567890", "12345678901234567890")]
    [InlineData("https://www.douyin.com/?%6Dodal_id=%31%32%33", "123")]
    [InlineData("https://www.douyin.com/jingxuan?modal_id=123", "123")]
    public void ExplicitSingleNumericModalNormalizesToDetailPage(string source, string id)
    {
        var result = Resolve(source);
        Assert.Equal("https://www.douyin.com/video/" + id, result.Url);
        Assert.Null(result.Message);
    }

    [Theory]
    [InlineData("https://www.douyin.com/video/7123456789012345678")]
    [InlineData("https://www.douyin.com/video/123?modal_id=456")]
    [InlineData("https://v.douyin.com/abc123/")]
    [InlineData("https://www.douyin.com/user/example")]
    [InlineData("https://www.douyin.com/discover?recommend=1")]
    [InlineData("https://example.test/?modal_id=123")]
    [InlineData("https://www.douyin.com.example.test/?modal_id=123")]
    [InlineData("https://other.douyin.com/?modal_id=123")]
    [InlineData("https://example.test/video.mp4?signature=secret")]
    [InlineData("https://www.bilibili.com/bangumi/play/ep323085")]
    [InlineData("https://www.bilibili.com/video/BV123")]
    public void LeavesOtherPagesForExistingParser(string source)
    {
        var result = Resolve(source);
        Assert.Equal(source, result.Url);
        Assert.Null(result.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("file:///C:/private.txt")]
    [InlineData("https://user:secret@www.douyin.com/")]
    public void InvalidBrowserSourceDoesNotBecomeParseTarget(string source)
    {
        var result = Resolve(source);
        Assert.Null(result.Url);
        Assert.NotNull(result.Message);
        Assert.DoesNotContain("secret", result.Message);
    }
}
