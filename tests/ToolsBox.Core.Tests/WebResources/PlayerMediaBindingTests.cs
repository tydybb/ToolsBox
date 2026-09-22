using ToolsBox.Core.WebResources;

namespace ToolsBox.Core.Tests.WebResources;

public class PlayerMediaBindingTests
{
    private static readonly PlayerSnapshot Player = new() { Id = "p1", Generation = 1, Source = "blob:https://www.douyin.com/a", SiteId = "123", Duration = 20, Visible = true };
    private static PlayerDocument Doc(PlayerSnapshot? player = null, string url = "https://www.douyin.com/?recommend=1") =>
        new() { Document = "document1", Url = url, Title = "title", Players = [player ?? Player] };
    private static Dictionary<string, DouyinMediaItem> Catalog() => new()
    {
        ["123"] = new("123", "当前视频", 20, [new("https://cdn.example/a.mp4", "720p", 720, 100)]),
        ["456"] = new("456", "下一条", 25, [new("https://cdn.example/b.mp4", "1080p", 1080, 200)])
    };

    [Fact]
    public void BlobBindsExactIdNotLatestRequest()
    {
        var result = PlayerMediaBinding.Resolve(Doc(), Player, new Dictionary<string, WebResourceKind>(), Catalog());
        Assert.NotNull(result); Assert.Equal("当前视频", result.Title);
        Assert.Equal("https://cdn.example/a.mp4", Assert.Single(result.Choices).Url);
        Assert.False(Assert.Single(result.Choices).RequiresPageExtraction);
    }

    [Theory]
    [InlineData("https://unrelated.example/")]
    [InlineData("https://douyin.com.attacker.example/")]
    public void OtherSitesCannotBorrowDouyinMetadata(string url) =>
        Assert.Null(PlayerMediaBinding.Resolve(Doc(url: url), Player, new Dictionary<string, WebResourceKind>(), Catalog()));

    [Fact]
    public void HiddenUnknownEncryptedAndWrongDurationAreRejected()
    {
        foreach (var p in new[] { Player with { Visible = false }, Player with { SiteId = "999" }, Player with { Encrypted = true },
            Player with { StreamObject = true }, Player with { Duration = 40 }, Player with { Source = "" }, Player with { Duration = 1e300 } })
            Assert.Null(PlayerMediaBinding.Resolve(Doc(p), p, new Dictionary<string, WebResourceKind>(), Catalog()));
    }

    [Fact]
    public void UntrustedNullFieldsAreRejected()
    {
        Assert.Null(PlayerMediaBinding.Resolve(Doc() with { Url = null! }, Player, new Dictionary<string, WebResourceKind>(), Catalog()));
        Assert.Null(PlayerMediaBinding.Resolve(Doc(), Player with { Source = null! }, new Dictionary<string, WebResourceKind>(), Catalog()));
        Assert.False(PlayerMediaBinding.IsSameTarget(Doc(), Player, Doc() with { Players = null! }));
    }

    [Fact]
    public void DirectSourceRequiresExactObservedMedia()
    {
        var p = Player with { Source = "https://cdn.example/first.mp4?token=1", SiteId = "" };
        var observed = new Dictionary<string, WebResourceKind> { ["https://cdn.example/first.mp4?token=2"] = WebResourceKind.Video };
        Assert.Null(PlayerMediaBinding.Resolve(Doc(p), p, observed, Catalog()));
        observed[p.Source] = WebResourceKind.Video;
        Assert.Equal(p.Source, Assert.Single(PlayerMediaBinding.Resolve(Doc(p), p, observed, Catalog())!.Choices).Url);
    }

    [Fact]
    public void NavigationAndSourceChangesInvalidateNativeConfirmation()
    {
        var before = Doc();
        Assert.True(PlayerMediaBinding.IsSameTarget(before, Player, Doc()));
        foreach (var after in new[] { Doc() with { Document = "document2" }, Doc() with { Url = "https://www.douyin.com/video/456" },
            Doc(Player with { Generation = 2 }), Doc(Player with { Source = "blob:new" }), Doc(Player with { SiteId = "456" }),
            Doc(Player with { Visible = false }), Doc() with { Players = [] } })
            Assert.False(PlayerMediaBinding.IsSameTarget(before, Player, after));
    }

    private static readonly PlayerSnapshot BilibiliPlayer = new()
    {
        Id = "p2", Generation = 2, Source = "blob:https://www.bilibili.com/current", SiteId = "323085", Duration = 1494, Visible = true
    };

    [Theory]
    [InlineData("https://www.bilibili.com/bangumi/play/ss33415?from_spmid=666.4.hotlist.0")]
    [InlineData("https://www.bilibili.com/bangumi/play/ep323085")]
    [InlineData("https://bilibili.com/bangumi/play/ss33415/")]
    public void BilibiliCurrentEpisodeBindsCanonicalSingleEpisodePage(string url)
    {
        var player = BilibiliPlayer with { Source = "blob:" + new Uri(url).GetLeftPart(UriPartial.Authority) + "/current" };
        var result = PlayerMediaBinding.Resolve(Doc(player, url), player, new Dictionary<string, WebResourceKind>(), Catalog());

        Assert.NotNull(result);
        Assert.Equal("title", result.Title);
        Assert.Equal(1494, result.Duration);
        var choice = Assert.Single(result.Choices);
        Assert.Equal("https://www.bilibili.com/bangumi/play/ep323085", choice.Url);
        Assert.Equal("当前单集·下载时解析并合并音视频", choice.Label);
        Assert.Equal(WebResourceKind.Video, choice.Kind);
        Assert.True(choice.RequiresPageExtraction);
    }

    [Theory]
    [InlineData("https://www.bilibili.com.attacker.example/bangumi/play/ss33415")]
    [InlineData("https://player.bilibili.com/bangumi/play/ss33415")]
    [InlineData("https://www.bilibili.com/video/BV123")]
    [InlineData("https://www.bilibili.com/bangumi/play/ep323086")]
    [InlineData("https://www.bilibili.com/bangumi/play/ss33415/ep323085")]
    [InlineData("https://www.bilibili.com/bangumi/play/ss33415x")]
    [InlineData("https://www.bilibili.com/bangumi/play/ss0")]
    [InlineData("https://www.bilibili.com/bangumi/play/ss033415")]
    [InlineData("https://www.bilibili.com/bangumi/play/ss123456789012345678901")]
    [InlineData("https://www.bilibili.com/bangumi/play/ss%33%33%34%31%35")]
    public void BilibiliRejectsUnrelatedMalformedOrConflictingPage(string url) =>
        Assert.Null(PlayerMediaBinding.Resolve(Doc(BilibiliPlayer, url), BilibiliPlayer, new Dictionary<string, WebResourceKind>(), Catalog()));

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0323085")]
    [InlineData("323085 ")]
    [InlineData("ep323085")]
    [InlineData("323085?other=1")]
    [InlineData("１２３")]
    [InlineData("123456789012345678901")]
    public void BilibiliRejectsMalformedEpisodeId(string siteId)
    {
        var player = BilibiliPlayer with { SiteId = siteId };
        Assert.Null(PlayerMediaBinding.Resolve(Doc(player, "https://www.bilibili.com/bangumi/play/ss33415"), player,
            new Dictionary<string, WebResourceKind>(), Catalog()));
    }

    [Fact]
    public void BilibiliRejectsHiddenEncryptedStreamAndInvalidDurationPlayers()
    {
        foreach (var player in new[] { BilibiliPlayer with { Visible = false }, BilibiliPlayer with { Encrypted = true },
            BilibiliPlayer with { StreamObject = true }, BilibiliPlayer with { Duration = null }, BilibiliPlayer with { Duration = 0 },
            BilibiliPlayer with { Duration = double.NaN }, BilibiliPlayer with { Duration = double.PositiveInfinity },
            BilibiliPlayer with { Duration = 1e300 }, BilibiliPlayer with { Source = "blob:https://unrelated.example/current" } })
            Assert.Null(PlayerMediaBinding.Resolve(Doc(player, "https://www.bilibili.com/bangumi/play/ss33415"), player,
                new Dictionary<string, WebResourceKind>(), Catalog()));
    }

    [Fact]
    public void BilibiliRequiresOneMatchingVisiblePlayerAndIgnoresHiddenPreloads()
    {
        var document = Doc(BilibiliPlayer, "https://www.bilibili.com/bangumi/play/ss33415");
        var other = BilibiliPlayer with { Id = "p3", SiteId = "323086" };
        foreach (var players in new PlayerSnapshot[]?[] { null, [], [BilibiliPlayer, other], [BilibiliPlayer, BilibiliPlayer],
            [BilibiliPlayer with { Generation = 3 }], [other] })
            Assert.Null(PlayerMediaBinding.Resolve(document with { Players = players! }, BilibiliPlayer,
                new Dictionary<string, WebResourceKind>(), Catalog()));

        Assert.NotNull(PlayerMediaBinding.Resolve(document with { Players = [BilibiliPlayer, other with { Visible = false }] },
            BilibiliPlayer, new Dictionary<string, WebResourceKind>(), Catalog()));
    }
}
