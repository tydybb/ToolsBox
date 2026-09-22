using System.Text.Json;
using ToolsBox.Core.WebResources;

namespace ToolsBox.Core.Tests.WebResources;

public class DouyinMediaParserTests
{
    [Theory]
    [InlineData("https://www.douyin.com/aweme/v1/web/aweme/detail/?aweme_id=123")]
    [InlineData("https://douyin.com/aweme/v1/web/feed/")]
    [InlineData("http://WWW.DOUYIN.COM/aweme/v1/web/tab/feed/?count=10")]
    [InlineData("https://www.douyin.com/aweme/v1/web/aweme/post/")]
    [InlineData("https://www.douyin.com/aweme/v1/web/aweme/related/")]
    [InlineData("https://www.douyin.com/aweme/v2/web/module/feed/?count=20")]
    public void RecognizesOnlyKnownVideoMetadataEndpoints(string url)
    {
        Assert.True(DouyinMediaParser.IsMetadataResponse(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/aweme/v1/web/aweme/detail/")]
    [InlineData("file://www.douyin.com/aweme/v1/web/aweme/detail/")]
    [InlineData("https://user:secret@www.douyin.com/aweme/v1/web/aweme/detail/")]
    [InlineData("https://@www.douyin.com/aweme/v1/web/aweme/detail/")]
    [InlineData("https://www.douyin.com.example.org/aweme/v1/web/aweme/detail/")]
    [InlineData("https://api.douyin.com/aweme/v1/web/aweme/detail/")]
    [InlineData("https://127.0.0.1/aweme/v1/web/aweme/detail/")]
    [InlineData("https://www.douyin.com/aweme/v1/web/user/profile/self/")]
    [InlineData("https://www.douyin.com/aweme/v1/web/aweme/favorite/")]
    [InlineData("https://www.douyin.com/aweme/v1/web/aweme/detail/extra")]
    [InlineData("https://www.douyin.com/video/123")]
    [InlineData("https://www.douyin.com/aweme/v1/web/aweme/detail/\n")]
    public void RejectsUnrelatedOrUnsafeMetadataEndpoints(string? url)
    {
        Assert.False(DouyinMediaParser.IsMetadataResponse(url!));
    }

    [Fact]
    public void ParsesDetailAndPreservesSignedUrlExactly()
    {
        const string signedUrl = "https://v.example.test/play/a%2Fb.mp4?token=secret%2Bvalue&x=1&x=2";
        var item = Assert.Single(Parse(new { aweme_detail = Video("123", signedUrl) }));

        Assert.Equal("123", item.Id);
        Assert.Equal("示例视频", item.Title);
        Assert.Equal(12.5, item.DurationSeconds);
        var variant = Assert.Single(item.Variants);
        Assert.Equal(signedUrl, variant.Url);
        Assert.Equal(720, variant.Height);
        Assert.Contains("720p", variant.Label);
    }

    [Fact]
    public void ParsesNestedFeedAndDetailWhileIgnoringUnrelatedUrls()
    {
        var items = Parse(new
        {
            data = new
            {
                aweme_list = new[] { Video("123"), Video("456") },
                more = new { aweme_detail = Video("789") },
                tracking = new { aweme_id = "999", url_list = new[] { "https://ad.example.test/a.mp4" } }
            },
            url = "https://other.example.test/not-a-video.mp4"
        });

        Assert.Equal(new[] { "123", "456", "789" }, items.Select(item => item.Id));
    }

    [Fact]
    public void DeduplicatesSignedUrlsAndOrdersBestKnownQualityFirst()
    {
        var items = Parse(new
        {
            aweme_detail = new
            {
                aweme_id = "123", desc = "quality",
                video = new
                {
                    height = 720, width = 1280,
                    play_addr = new { url_list = new[] { "https://v.example.test/shared", "https://v.example.test/low" } },
                    bit_rate = new[]
                    {
                        new { bit_rate = 1_000_000L, gear_name = "standard", play_addr = new { height = 720, width = 1280, url_list = new[] { "https://v.example.test/shared" } } },
                        new { bit_rate = 3_000_000L, gear_name = "high", play_addr = new { height = 1080, width = 1920, url_list = new[] { "https://v.example.test/high" } } },
                        new { bit_rate = 2_000_000L, gear_name = "better", play_addr = new { height = 720, width = 1280, url_list = new[] { "https://v.example.test/shared" } } }
                    }
                }
            }
        });

        var variants = Assert.Single(items).Variants;
        Assert.Equal(3, variants.Count);
        Assert.Equal(new[] { 1080, 720, 720 }, variants.Select(variant => variant.Height));
        Assert.Equal(new[] { 3_000_000L, 2_000_000L, 0L }, variants.Select(variant => variant.BitRate));
        Assert.Contains("high", variants[0].Label);
        Assert.Contains("1080p", variants[0].Label);
    }

    [Fact]
    public void MergesRepeatedIdsWithoutCombiningDifferentVideos()
    {
        var items = Parse(new { aweme_list = new[]
        {
            Video("123", "https://v.example.test/a"),
            Video("123", "https://v.example.test/b"),
            Video("456", "https://v.example.test/c")
        } });

        Assert.Equal(2, items.Count);
        Assert.Equal(2, items[0].Variants.Count);
        Assert.Single(items[1].Variants);
        Assert.DoesNotContain(items[0].Variants, variant => variant.Url.EndsWith("/c"));
    }

    [Fact]
    public void PrefersSuppliedOfficialPlayUrlOnlyWithinSameQualityAndPreservesSignature()
    {
        const string official = "https://www.douyin.com/aweme/v1/play/?video_id=123&file_id=a%2Fb&token=x%2By&x=1&x=2";
        var item = Assert.Single(Parse(new { aweme_detail = new { aweme_id = "123", video = new { bit_rate = new[]
        {
            new { bit_rate = 200L, play_addr = new { height = 1080, url_list = new[] { "https://cdn.example/high", official } } },
            new { bit_rate = 100L, play_addr = new { height = 1080, url_list = new[] { "https://www.douyin.com/aweme/v1/play/?video_id=123&file_id=lower" } } },
            new { bit_rate = 300L, play_addr = new { height = 720, url_list = new[] { "https://www.douyin.com/aweme/v1/play/?video_id=123&file_id=small" } } }
        } } } }));
        Assert.Equal(official, item.Variants[0].Url);
        Assert.Equal("https://cdn.example/high", item.Variants[1].Url);
        Assert.Equal(new[] { 1080, 1080, 1080, 720 }, item.Variants.Select(v => v.Height));
        Assert.Equal(new[] { 200L, 200L, 100L, 300L }, item.Variants.Select(v => v.BitRate));
    }

    [Theory]
    [InlineData("http://www.douyin.com/aweme/v1/play/?video_id=123")]
    [InlineData("https://www.douyin.com.attacker.test/aweme/v1/play/?video_id=123")]
    [InlineData("https://api.douyin.com/aweme/v1/play/?video_id=123")]
    [InlineData("https://www.douyin.com:8443/aweme/v1/play/?video_id=123")]
    [InlineData("https://www.douyin.com/aweme/v1/play/other?video_id=123")]
    public void UnrelatedPlayUrlDoesNotReceivePreference(string url)
    {
        var item = Assert.Single(Parse(new { aweme_detail = new { aweme_id = "123", video = new
        { play_addr = new { url_list = new[] { "https://cdn.example/first", url } } } } }));
        Assert.Equal("https://cdn.example/first", item.Variants[0].Url);
    }

    [Fact]
    public void OfficialPlayUrlSurvivesVariantLimitWhenQualityIsEqual()
    {
        const string official = "https://www.douyin.com/aweme/v1/play/?video_id=123";
        var item = Assert.Single(Parse(new { aweme_detail = new { aweme_id = "123", video = new
        { play_addr = new { url_list = Enumerable.Range(0,32).Select(i => $"https://cdn.example/{i}").Append(official).ToArray() } } } }));
        Assert.Equal(32, item.Variants.Count);
        Assert.Equal(official, item.Variants[0].Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123456789012345678901")]
    [InlineData("１２３")]
    [InlineData("١٢٣")]
    [InlineData("-123")]
    [InlineData("123\n")]
    [InlineData("123/456")]
    public void RejectsInvalidVideoIds(string id)
    {
        Assert.Empty(Parse(new { aweme_detail = Video(id) }));
    }

    [Theory]
    [InlineData("file:///C:/private.mp4")]
    [InlineData("blob:https://www.douyin.com/random")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://name:password@v.example.test/play")]
    [InlineData("https://@v.example.test/play")]
    [InlineData("https://v.example.test/play\n?signature=secret")]
    [InlineData("https://v.example.test/play?signature=%0d%0a")]
    [InlineData("//v.example.test/play")]
    public void RejectsUnsafeMediaUrls(string url)
    {
        Assert.Empty(Parse(new { aweme_detail = Video("123", url) }));
    }

    [Theory]
    [InlineData("is_drm", "true")]
    [InlineData("has_drm", "1")]
    [InlineData("drm_type", "\"widevine\"")]
    [InlineData("drm_type", "2")]
    [InlineData("is_encrypted", "true")]
    [InlineData("is_encrypt", "1")]
    public void SkipsExplicitDrmOrEncryptedVideo(string flag, string value)
    {
        string json = """{"aweme_detail":{"aweme_id":"123","video":{ """ +
            JsonSerializer.Serialize(flag) + ":" + value +
            """, "play_addr":{"url_list":["https://v.example.test/play"]}}}}""";
        Assert.Empty(DouyinMediaParser.Parse(json));
    }

    [Fact]
    public void RejectsDrmOnItemButRetainsClearVariantsAlongsideEncryptedVariant()
    {
        string json = """
            {"aweme_list":[
              {"aweme_id":"123","is_drm":true,"video":{"play_addr":{"url_list":["https://v.example.test/item-drm"]}}},
              {"aweme_id":"456","video":{"is_drm":false,"has_drm":0,"drm_type":0,"is_h265":1,"bit_rate":[
                {"is_encrypted":true,"play_addr":{"url_list":["https://v.example.test/encrypted"]}},
                {"bit_rate":1000000,"play_addr":{"is_drm":false,"url_list":["https://v.example.test/clear"]}}
              ]}}
            ]}
            """;

        var item = Assert.Single(DouyinMediaParser.Parse(json));
        Assert.Equal("456", item.Id);
        Assert.Equal("https://v.example.test/clear", Assert.Single(item.Variants).Url);
    }

    [Theory]
    [InlineData("\"images\":[{\"url_list\":[\"https://v.example.test/image.jpg\"]}]")]
    [InlineData("\"aweme_type\":68")]
    [InlineData("\"aweme_type\":101")]
    [InlineData("\"is_live\":true")]
    [InlineData("\"is_image\":true")]
    public void SkipsImageAndLiveItemsEvenWhenPreviewVideoExists(string field)
    {
        string json = """{"aweme_detail":{"aweme_id":"123", """ + field +
            """, "video":{"play_addr":{"url_list":["https://v.example.test/preview"]}}}}""";
        Assert.Empty(DouyinMediaParser.Parse(json));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"aweme_detail\":123}")]
    [InlineData("{\"aweme_detail\":{\"aweme_id\":\"123\"}}")]
    [InlineData("{\"aweme_list\":{\"aweme_id\":\"123\"}}")]
    [InlineData("{\"aweme_detail\":{\"aweme_id\":123,\"video\":{}}}")]
    public void MalformedOrUnrecognizedShapesReturnEmpty(string? json)
    {
        Assert.Empty(DouyinMediaParser.Parse(json!));
    }

    [Theory]
    [InlineData("{\"aweme_detail\":{\"aweme_id\":\"123\",\"desc\":\"\\uD800\",\"video\":{\"play_addr\":{\"url_list\":[\"https://v.example.test/play\"]}}}}")]
    [InlineData("{\"aweme_detail\":{\"aweme_id\":\"123\",\"\\uD800\":true,\"video\":{\"play_addr\":{\"url_list\":[\"https://v.example.test/play\"]}}}}")]
    public void InvalidUnicodeEscapesReturnEmptyWithoutThrowing(string json)
    {
        Assert.Empty(DouyinMediaParser.Parse(json));
    }

    [Fact]
    public void BoundsJsonSizeAndDepth()
    {
        string nested = JsonSerializer.Serialize(new { aweme_detail = Video("123") });
        for (int i = 0; i < 33; i++) nested = "{\"data\":" + nested + "}";

        Assert.Empty(DouyinMediaParser.Parse(nested));
        Assert.Empty(DouyinMediaParser.Parse(new string(' ', 4 * 1024 * 1024 + 1)));
        string unicode = "{\"padding\":\"" + new string('中', 1_500_000) + "\",\"aweme_detail\":" + JsonSerializer.Serialize(Video("123")) + "}";
        Assert.True(unicode.Length < 4 * 1024 * 1024);
        Assert.Empty(DouyinMediaParser.Parse(unicode));
    }

    [Fact]
    public void BoundsRecordsAndVariants()
    {
        var items = Parse(new { aweme_list = Enumerable.Range(1, 101).Select(i => Video(i.ToString())).ToArray() });
        Assert.Equal(100, items.Count);

        var item = Assert.Single(Parse(new
        {
            aweme_detail = new
            {
                aweme_id = "123",
                video = new { play_addr = new { url_list = Enumerable.Range(1, 100).Select(i => $"https://v.example.test/{i}").ToArray() } }
            }
        }));
        Assert.Equal(32, item.Variants.Count);
    }

    [Fact]
    public void BoundsSanitizedTitleAndTreatsInvalidDurationAsUnknown()
    {
        var item = Assert.Single(Parse(new
        {
            aweme_detail = new
            {
                aweme_id = "123", desc = "  标题\r\n\t\0" + new string('长', 300),
                video = new { duration = -1, play_addr = new { url_list = new[] { "https://v.example.test/play" } } }
            }
        }));

        Assert.StartsWith("标题", item.Title);
        Assert.InRange(item.Title.Length, 1, 200);
        Assert.DoesNotContain(item.Title, char.IsControl);
        Assert.Null(item.DurationSeconds);
    }

    private static IReadOnlyList<DouyinMediaItem> Parse(object payload) =>
        DouyinMediaParser.Parse(JsonSerializer.Serialize(payload));

    private static object Video(string id, string url = "https://v.example.test/play") => new
    {
        aweme_id = id,
        desc = "示例视频",
        video = new { duration = 12500, height = 720, width = 1280, play_addr = new { url_list = new[] { url } } }
    };
}
