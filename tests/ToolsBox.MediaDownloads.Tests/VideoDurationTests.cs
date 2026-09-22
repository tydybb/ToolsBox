using ToolsBox.MediaDownloads;

namespace ToolsBox.MediaDownloads.Tests;

public sealed class VideoDurationTests
{
    [Fact]
    public void DownloadKeepsExistingArgumentsAndAppendsOptionalExpectedDuration()
    {
        var parameters = typeof(MediaDownloadService).GetMethod(nameof(MediaDownloadService.DownloadVideoAsync))!.GetParameters();
        Assert.Equal(new[] { "uri", "outputDirectory", "formatId", "progress", "ct", "cookies", "referer", "userAgent", "expectedDurationSeconds" },
            parameters.Select(p => p.Name));
        Assert.Equal(typeof(double?), parameters[^1].ParameterType);
        Assert.True(parameters[^1].IsOptional);
        Assert.Null(parameters[^1].DefaultValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.MaxValue)]
    public async Task InvalidExpectedDurationIsRejectedBeforeComponentLookup(double expected)
    {
        var output = Path.Combine(Path.GetTempPath(), "ToolsBox-duration-tests-" + Guid.NewGuid().ToString("N"));
        var service = new MediaDownloadService(new ComponentManager(Path.Combine(output, "components")));
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.DownloadVideoAsync(new Uri("https://example.org/video"), output, expectedDurationSeconds: expected));
        Assert.Equal("expectedDurationSeconds", exception.ParamName);
        Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(28, 30)]
    [InlineData(32, 30)]
    [InlineData(980, 1000)]
    [InlineData(1020, 1000)]
    [InlineData(120.5, 120)]
    [InlineData(0.25, 0.25)]
    [InlineData(86400, 86400)]
    [InlineData(172800, 172800)]
    public void MatchingDurationAcceptsTwoSecondsOrTwoPercent(double parsed, double expected)
    {
        new VideoInfo("episode", [], parsed, true).ValidateExpectedDuration(expected);
    }

    [Theory]
    [InlineData(30, 1440)]
    [InlineData(1500, 1440)]
    [InlineData(27.99, 30)]
    [InlineData(32.01, 30)]
    [InlineData(979.99, 1000)]
    [InlineData(1020.01, 1000)]
    public void AdvertisingOrDifferentEpisodeDurationIsRejected(double parsed, double expected)
    {
        var exception = Assert.Throws<InvalidDataException>(() => new VideoInfo("https://example.org/?token=private", [], parsed).ValidateExpectedDuration(expected));
        Assert.Contains("播放器", exception.Message);
        Assert.DoesNotContain("private", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.MaxValue)]
    public void UnreliableParsedDurationIsRejectedWhenPlayerDurationIsKnown(double? parsed)
    {
        Assert.Throws<InvalidDataException>(() => new VideoInfo("episode", [], parsed).ValidateExpectedDuration(86400));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"duration\":null}")]
    [InlineData("{\"duration\":\"unknown\"}")]
    [InlineData("{\"duration\":1e400}")]
    public void MissingOrUnreliableExtractorDurationCannotVerifyPlayer(string json)
    {
        Assert.Throws<InvalidDataException>(() => VideoInfo.Parse(json).ValidateExpectedDuration(1440));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.MaxValue)]
    public void OmittedExpectedDurationPreservesExistingBehavior(double? parsed)
    {
        new VideoInfo("legacy", [], parsed).ValidateExpectedDuration(null);
    }

    [Fact]
    public async Task ExistingCallerWithoutExpectedDurationStillReachesComponentCheck()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new MediaDownloadService(new ComponentManager(directory));
        var exception = await Assert.ThrowsAsync<MediaDownloadException>(() => service.DownloadVideoAsync(new Uri("https://example.org/video"), directory));
        Assert.Equal(MediaDownloadError.ComponentsMissing, exception.Code);
        Assert.False(Directory.Exists(directory));
    }

}
