using ToolsBox.MediaDownloads;
using System.Net;
namespace ToolsBox.MediaDownloads.Tests;

public class EngineTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("\"unknown\"")]
    [InlineData("false")]
    [InlineData("{}")]
    public void ParseTreatsNullAndNonNumericOptionalNumbersAsUnknown(string value)
    {
        var info = VideoInfo.Parse("{\"duration\":" + value + ",\"formats\":[{\"format_id\":\"video\",\"vcodec\":\"h264\",\"height\":" + value + ",\"ext\":\"mp4\"}]}");
        Assert.Null(info.DurationSeconds);
        Assert.Null(Assert.Single(info.Formats).Height);
    }

    [Fact]
    public void ParseTreatsNullDurationWithoutFormatsAsUnknown()
    {
        Assert.Null(VideoInfo.Parse("""{"duration":null}""").DurationSeconds);
    }

    [Fact]
    public async Task UserAgentControlCharactersAreRejectedBeforeNetwork()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var handler = new HeaderReply();
        try
        {
            var service = new MediaDownloadService(new ComponentManager(dir), new HttpClient(handler));
            await Assert.ThrowsAsync<ArgumentException>(() => service.DownloadImageAsync(new Uri("https://example.org/image"), dir, userAgent: "Browser\r\nAuthorization: secret"));
            Assert.Null(handler.Agent);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task WindowsReservedNameBecomesOrdinaryFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var service = new MediaDownloadService(new ComponentManager(dir), new HttpClient(new HeaderReply()));
            var path = await service.DownloadImageAsync(new Uri("https://example.org/image"), dir, "CON");
            Assert.Equal("_CON.png", Path.GetFileName(path)); Assert.True(File.Exists(path));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task ImageHeadersPreserveOnlyOriginAndSafeAgent()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var handler = new HeaderReply();
        try
        {
            var service = new MediaDownloadService(new ComponentManager(dir), new HttpClient(handler));
            await service.DownloadImageAsync(new Uri("https://cdn.example/picture"), dir, referer: "https://user:password@page.example/secret?token=private", userAgent: "TestBrowser/1");
            Assert.Equal("https://page.example/", handler.Referer); Assert.Equal("TestBrowser/1", handler.Agent);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    private sealed class HeaderReply : HttpMessageHandler
    {
        public string? Referer; public string? Agent;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Referer = request.Headers.Referrer?.AbsoluteUri; Agent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/R8AAAAASUVORK5CYII=")) });
        }
    }
    [Fact]
    public void NonPortableMp4CodecsRequireConversion()
    {
        Assert.False(VideoInfo.CanRemuxToMp4("""{"streams":[{"codec_type":"video","codec_name":"ffv1"},{"codec_type":"audio","codec_name":"pcm_s16le"}]}"""));
        Assert.True(VideoInfo.CanRemuxToMp4("""{"streams":[{"codec_type":"video","codec_name":"h264"},{"codec_type":"audio","codec_name":"aac"}]}"""));
    }
    [Fact]
    public void ComponentErrorsAreSanitizedAndActionable()
    {
        var error = MediaDownloadException.FromProcess(MediaDownloadError.DownloadFailed, "ERROR HTTP Error 403 at https://secret.example/a?token=private");
        Assert.Equal(MediaDownloadError.LoginRequired, error.Code);
        Assert.DoesNotContain("private", error.Message);
        Assert.Contains("登录", error.Message);
    }
    [Fact]
    public void ComponentSourcesAreVersionLocked()
    {
        Assert.Contains("/2026.08.19/", ComponentManager.YtDlpSource);
        Assert.Contains("ffmpeg-9.0.1-essentials_build.zip", ComponentManager.FFmpegSource);
    }
    [Fact]
    public async Task StructurallyValidJpegRemainsSupported()
    {
        const string jpeg = "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD8qqKKKAP/2Q==";
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var service = new MediaDownloadService(new ComponentManager(dir), new HttpClient(new Reply(Convert.FromBase64String(jpeg))));
            Assert.EndsWith(".jpg", await service.DownloadImageAsync(new Uri("https://example.org/a"), dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Theory]
    [InlineData("/9j/2Q==")]
    [InlineData("UklGRggAAABXRUJQAAAAAA==")]
    public async Task HeaderOnlyImagesAreRejected(string base64)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try { var service = new MediaDownloadService(new ComponentManager(dir), new HttpClient(new Reply(Convert.FromBase64String(base64)))); await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadImageAsync(new Uri("https://example.org/a"), dir)); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public void ParseRetainsDurationAndAvailableAudio()
    {
        var info = VideoInfo.Parse("""{"duration":120.5,"formats":[{"format_id":"audio","vcodec":"none","acodec":"aac"}]}""");
        Assert.Equal(120.5, info.DurationSeconds); Assert.True(info.HasAudio);
    }
    [Theory]
    [InlineData("0", true)]
    [InlineData("NaN", true)]
    [InlineData("30", true)]
    [InlineData("120", false)]
    public void OutputRejectsTruncationInvalidDurationOrMissingAudio(string duration, bool audio)
    {
        var json = "{\"format\":{\"format_name\":\"mov,mp4\",\"duration\":\"" + duration + "\"},\"streams\":[{\"codec_type\":\"video\"}" + (audio ? ",{\"codec_type\":\"audio\"}" : "") + "]}";
        Assert.Throws<InvalidDataException>(() => new VideoInfo("video", Array.Empty<VideoFormat>(), 120, true).ValidateOutput(json));
    }
    [Fact]
    public void OutputAcceptsMinorDurationRoundingAndIntentionalSilentVideo()
    {
        new VideoInfo("video", Array.Empty<VideoFormat>(), 120, false).ValidateOutput("""{"format":{"format_name":"mov,mp4","duration":"119.8"},"streams":[{"codec_type":"video"}]}""");
    }
    [Fact]
    public async Task InstallerNeverExtractsArbitraryArchivePaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        using var zipBytes = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(zipBytes, System.IO.Compression.ZipArchiveMode.Create, true)) { var entry = zip.CreateEntry("../../outside.txt"); using var writer = new StreamWriter(entry.Open()); writer.Write("escape"); }
        try { var manager = new ComponentManager(Path.Combine(dir, "components"), new HttpClient(new ArchiveReply(zipBytes.ToArray()))); await Assert.ThrowsAsync<InvalidDataException>(() => manager.InstallAsync()); Assert.False(File.Exists(Path.Combine(dir, "outside.txt"))); Assert.Null(await manager.GetInstalledAsync()); }
        finally { Directory.Delete(dir, true); }
    }
    private sealed class ArchiveReply(byte[] archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var exe = new byte[] { 1, 2, 3 }; var path = r.RequestUri!.AbsolutePath;
            HttpContent content = path.EndsWith("SHA2-256SUMS") ? new StringContent(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(exe)) + "  yt-dlp.exe") : path.EndsWith(".sha256") ? new StringContent(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archive))) : new ByteArrayContent(path.EndsWith(".zip") ? archive : exe);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = r, Content = content });
        }
    }
    [Fact]
    public async Task InstallerRejectsChecksumAndDoesNotActivate()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try { var manager = new ComponentManager(dir, new HttpClient(new BadChecksum())); await Assert.ThrowsAsync<InvalidDataException>(() => manager.InstallAsync()); Assert.Null(await manager.GetInstalledAsync()); Assert.Empty(Directory.GetFileSystemEntries(dir)); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    private sealed class BadChecksum : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = r, Content = r.RequestUri!.AbsolutePath.EndsWith("SHA2-256SUMS") ? new StringContent(new string('0', 64) + "  yt-dlp.exe") : new ByteArrayContent(new byte[] { 1, 2, 3 }) });
    }
    [Fact]
    public async Task TruncatedPngIsRejected()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAAB");
        try { var service = new MediaDownloadService(new ComponentManager(dir), new HttpClient(new Reply(bytes))); await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadImageAsync(new Uri("https://example.org/a.png"), dir)); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task VideoRequiresExplicitComponentSetup()
    {
        var service = new MediaDownloadService(new ComponentManager(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var error = await Assert.ThrowsAsync<MediaDownloadException>(() => service.InspectVideoAsync(new Uri("https://example.org/video")));
        Assert.Equal(MediaDownloadError.ComponentsMissing, error.Code);
    }
    [Fact]
    public async Task VideoRejectsFormatExpressionInjection()
    {
        var service = new MediaDownloadService(new ComponentManager(Path.GetTempPath()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DownloadVideoAsync(new Uri("https://example.org/video"), Path.GetTempPath(), "best;evil"));
    }
    [Fact]
    public async Task ProcessRunsOwnChildAndCapturesOutput()
    {
        var result = await ProcessRunner.RunAsync("cmd.exe", new[] { "/d", "/c", "echo safe-output" });
        Assert.Equal(0, result.ExitCode); Assert.Contains("safe-output", result.Output);
    }
    [Fact]
    public async Task ProcessCancellationStopsOwnedChild()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessRunner.RunAsync("ping.exe", new[] { "-n", "30", "127.0.0.1" }, cancel.Token));
    }
    [Fact]
    public async Task ImportRejectsIncompleteDirectoryWithoutActivation()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try { var manager = new ComponentManager(Path.Combine(dir, "installed")); await Assert.ThrowsAsync<FileNotFoundException>(() => manager.ImportAsync(dir)); Assert.Null(await manager.GetInstalledAsync()); }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task ImageRejectsHtmlAndLeavesNoFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var service = new MediaDownloadService(new ComponentManager(dir), new HttpClient(new Reply("<html>login</html>"u8.ToArray())));
            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadImageAsync(new Uri("https://example.org/a.png"), dir, "test"));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task ImagePreservesExistingAndUsesDetectedExtension()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "photo.png"), "keep");
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a/R8AAAAASUVORK5CYII=");
            var service = new MediaDownloadService(new ComponentManager(dir), new HttpClient(new Reply(png)));
            var path = await service.DownloadImageAsync(new Uri("https://example.org/a"), dir, "photo");
            Assert.EndsWith(".png", path); Assert.NotEqual(Path.Combine(dir, "photo.png"), path);
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(dir, "photo.png")));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact] public void ParseVideoRejectsPlaylist() => Assert.Throws<InvalidDataException>(() => VideoInfo.Parse("{\"_type\":\"playlist\",\"entries\":[]}"));
    [Fact]
    public void ParseVideoOmitsAudioOnlyAndDrm()
    {
        var info = VideoInfo.Parse("""{"title":"hello","formats":[{"format_id":"a","vcodec":"none"},{"format_id":"b","vcodec":"h264","height":1080,"ext":"mp4"},{"format_id":"c","vcodec":"h264","has_drm":true}]}""");
        Assert.Equal("b", Assert.Single(info.Formats).Id);
    }
    [Fact]
    public async Task MissingComponentsAreNotInstalledImplicitly()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Null(await new ComponentManager(dir).GetInstalledAsync()); Assert.False(Directory.Exists(dir));
    }
    private sealed class Reply(byte[] bytes) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); }
}
