using System.Net;
using ToolsBox.MediaDownloads;

namespace ToolsBox.MediaDownloads.Tests;

public sealed class CapturedVideoTests
{
    [Fact]
    public async Task RequiresComponentsBeforeSendingAnyRequest()
    {
        using var fixture = new Fixture(installed: false);
        var error = await Assert.ThrowsAsync<MediaDownloadException>(() => fixture.Download());
        Assert.Equal(MediaDownloadError.ComponentsMissing, error.Code);
        Assert.Empty(fixture.Requests);
        Assert.False(Directory.Exists(fixture.Output));
    }

    [Theory]
    [InlineData("file:///C:/secret.mp4")]
    [InlineData("https://user:secret@video.example/movie")]
    public async Task RejectsUnsafeSourceBeforeNetwork(string url)
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Download(url));
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async Task HeadersContainOnlyMatchingCookiesAndOriginReferer()
    {
        using var fixture = new Fixture();
        var cookies = new[]
        {
            Cookie("host", "yes", "video.example", "/media", hostOnly: true),
            Cookie("domain", "yes", ".video.example", "/", hostOnly: false),
            Cookie("", "bare-token", "video.example", "/"),
            Cookie("other", "secret", "elsewhere.example", "/"),
            Cookie("path", "secret", "video.example", "/media2"),
            Cookie("old", "secret", "video.example", "/") with { Expires = DateTimeOffset.UtcNow.AddDays(-1) }
        };
        await Assert.ThrowsAsync<MediaDownloadException>(() => fixture.Download(cookies: cookies,
            referer: "https://page.example/private/path?secret=private", userAgent: "TestBrowser/1"));
        var request = Assert.Single(fixture.Requests);
        Assert.Equal("host=yes; domain=yes; bare-token", request.Cookie);
        Assert.Equal("https://page.example/", request.Referer);
        Assert.Equal("TestBrowser/1", request.UserAgent);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Output));
    }

    [Fact]
    public async Task RedirectRecomputesCookieScopeAndNeverForwardsHostOnlyCookie()
    {
        using var fixture = new Fixture((request, index) => index == 0
            ? Redirect("https://child.video.example/media/movie") : new(HttpStatusCode.Forbidden));
        var cookies = new[]
        {
            Cookie("host", "private", "video.example", "/"),
            Cookie("domain", "allowed", ".video.example", "/", hostOnly: false),
            Cookie("child", "allowed", "child.video.example", "/")
        };
        await Assert.ThrowsAsync<MediaDownloadException>(() => fixture.Download(cookies: cookies));
        Assert.Equal(2, fixture.Requests.Count);
        Assert.Equal("host=private; domain=allowed", fixture.Requests[0].Cookie);
        Assert.Equal("domain=allowed; child=allowed", fixture.Requests[1].Cookie);
    }

    [Fact]
    public async Task RedirectToDifferentHostReceivesNoCookie()
    {
        using var fixture = new Fixture((request, index) => index == 0
            ? Redirect("https://other.example/media/movie") : new(HttpStatusCode.Forbidden));
        await Assert.ThrowsAsync<MediaDownloadException>(() => fixture.Download(cookies: [Cookie("session", "private", ".video.example", "/", hostOnly: false)]));
        Assert.Equal(2, fixture.Requests.Count);
        Assert.Null(fixture.Requests[1].Cookie);
    }

    [Theory]
    [InlineData("file:///C:/secret.mp4")]
    [InlineData("https://user:secret@other.example/movie")]
    [InlineData("http://video.example/movie")]
    public async Task RejectsUnsafeOrDowngradeRedirect(string destination)
    {
        using var fixture = new Fixture((_, _) => Redirect(destination));
        var error = await Assert.ThrowsAsync<MediaDownloadException>(() => fixture.Download());
        Assert.Equal(MediaDownloadError.DownloadFailed, error.Code);
        Assert.Single(fixture.Requests);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Output));
    }

    [Fact]
    public async Task RedirectLoopStopsAfterTenRedirects()
    {
        using var fixture = new Fixture((_, index) => Redirect("https://video.example/media/" + index));
        var error = await Assert.ThrowsAsync<MediaDownloadException>(() => fixture.Download());
        Assert.Equal(MediaDownloadError.DownloadFailed, error.Code);
        Assert.Equal(11, fixture.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, MediaDownloadError.LoginRequired)]
    [InlineData(HttpStatusCode.Forbidden, MediaDownloadError.LoginRequired)]
    [InlineData(HttpStatusCode.TooManyRequests, MediaDownloadError.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, MediaDownloadError.DownloadFailed)]
    public async Task HttpFailuresAreSanitized(HttpStatusCode status, MediaDownloadError expected)
    {
        using var fixture = new Fixture((_, _) => new(status) { Content = new StringContent("secret-cookie-or-signed-url") });
        var error = await Assert.ThrowsAsync<MediaDownloadException>(() => fixture.Download("https://video.example/media/movie?token=private"));
        Assert.Equal(expected, error.Code);
        Assert.DoesNotContain("private", error.ToString());
        Assert.DoesNotContain("secret-cookie", error.ToString());
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Output));
    }

    [Fact]
    public async Task AdvertisedOversizeIsRejectedWithoutReadingBody()
    {
        using var fixture = new Fixture((_, _) =>
        {
            var content = new ByteArrayContent([1]);
            content.Headers.ContentLength = 10L * 1024 * 1024 * 1024 + 1;
            return new(HttpStatusCode.OK) { Content = content };
        });
        var error = await Assert.ThrowsAsync<MediaDownloadException>(() => fixture.Download());
        Assert.Equal(MediaDownloadError.DownloadFailed, error.Code);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Output));
    }

    [Fact]
    public async Task PartialResponseIsRejectedAsIncomplete()
    {
        using var fixture = new Fixture((_, _) => new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([1, 2, 3]) });
        var error = await Assert.ThrowsAsync<MediaDownloadException>(() => fixture.Download());
        Assert.Equal(MediaDownloadError.DownloadFailed, error.Code);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Output));
    }

    [Fact]
    public async Task TruncatedBodyIsRejectedBeforeProbeAndRemoved()
    {
        using var fixture = new Fixture((_, _) =>
        {
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentLength = 100;
            return new(HttpStatusCode.OK) { Content = content };
        });
        var error = await Assert.ThrowsAsync<MediaDownloadException>(() => fixture.Download());
        Assert.Equal(MediaDownloadError.DownloadFailed, error.Code);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Output));
    }

    [Fact]
    public async Task CancellationRemovesOwnedStageButPreservesOtherFiles()
    {
        using var cancel = new CancellationTokenSource();
        using var fixture = new Fixture((_, _) => { cancel.Cancel(); return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) }; });
        Directory.CreateDirectory(fixture.Output);
        var keep = Path.Combine(fixture.Output, "keep.txt");
        File.WriteAllText(keep, "preserved");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Download(ct: cancel.Token));
        Assert.Equal(new[] { keep }, Directory.GetFileSystemEntries(fixture.Output));
        Assert.Equal("preserved", File.ReadAllText(keep));
    }

    [Theory]
    [InlineData("bad\r\nHeader", "value")]
    [InlineData("valid", "value; injected=secret")]
    [InlineData("valid", "value\nHeader")]
    public async Task InvalidCookieHeadersAreRejectedBeforeNetwork(string name, string value)
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Download(cookies: [Cookie(name, value, "video.example", "/")]));
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async Task SecureCookiesAreNotSentOverHttp()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<MediaDownloadException>(() => fixture.Download("http://video.example/media/movie",
            cookies: [Cookie("secure", "private", "video.example", "/") with { Secure = true }, Cookie("plain", "public", "video.example", "/")]));
        Assert.Equal("plain=public", Assert.Single(fixture.Requests).Cookie);
    }

    [Fact]
    public async Task MoreSpecificCookiePathsPrecedeRootCookiesWithSameName()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<MediaDownloadException>(() => fixture.Download(cookies:
            [Cookie("session", "root", "video.example", "/"), Cookie("session", "specific", "video.example", "/media")]));
        Assert.Equal("session=specific; session=root", Assert.Single(fixture.Requests).Cookie);
    }

    [Fact]
    public async Task ThrowsSanitizedDownloadErrorForNetworkFailureAndPreservesFiles()
    {
        using var fixture = new Fixture((_, _) => throw new HttpRequestException("Cannot fetch https://video.example/movie?token=private"));
        var error = await Assert.ThrowsAsync<MediaDownloadException>(() => fixture.Download());
        Assert.Equal(MediaDownloadError.DownloadFailed, error.Code);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private", error.ToString());
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Output));
    }

    [Fact]
    public async Task HostSuffixMustMatchAtLabelBoundary()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<MediaDownloadException>(() => fixture.Download("https://evilvideo.example/media/movie",
            cookies: [Cookie("session", "private", ".video.example", "/", hostOnly: false)]));
        Assert.Null(Assert.Single(fixture.Requests).Cookie);
    }

    private static MediaCookie Cookie(string name, string value, string domain, string path, bool hostOnly = true) =>
        new(name, value, domain, path, false, null, hostOnly);

    private static HttpResponseMessage Redirect(string uri)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(uri);
        return response;
    }

    private sealed record RequestSnapshot(string Uri, string? Cookie, string? Referer, string UserAgent);
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ToolsBox-captured-tests-" + Guid.NewGuid().ToString("N"));
        private readonly HttpClient _client;
        private readonly MediaDownloadService _service;
        public List<RequestSnapshot> Requests { get; } = [];
        public string Output => Path.Combine(_root, "output");

        public Fixture(Func<HttpRequestMessage, int, HttpResponseMessage>? response = null, bool installed = true)
        {
            var components = Path.Combine(_root, "components");
            if (installed)
            {
                var id = Guid.NewGuid().ToString("N");
                var files = Path.Combine(components, id);
                Directory.CreateDirectory(files);
                File.WriteAllText(Path.Combine(components, "active.txt"), id);
                // Tests terminate during HTTP validation, so no fake executable is ever run.
                foreach (var file in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe" }) File.WriteAllBytes(Path.Combine(files, file), []);
            }
            _client = new HttpClient(new Reply((request, index) =>
            {
                Requests.Add(new(request.RequestUri!.AbsoluteUri,
                    request.Headers.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : null,
                    request.Headers.Referrer?.AbsoluteUri, request.Headers.UserAgent.ToString()));
                return response?.Invoke(request, index) ?? new(HttpStatusCode.Forbidden);
            }));
            _service = new(new ComponentManager(components), _client);
        }

        public Task<string> Download(string url = "https://video.example/media/movie", IReadOnlyList<MediaCookie>? cookies = null,
            string? referer = null, string? userAgent = null, CancellationToken ct = default)
        {
            return _service.DownloadCapturedVideoAsync(new Uri(url), Output, "sample", ct: ct,
                cookies: cookies, referer: referer, userAgent: userAgent);
        }

        public void Dispose()
        {
            _client.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private sealed class Reply(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int _index;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request, _index++));
    }
}
