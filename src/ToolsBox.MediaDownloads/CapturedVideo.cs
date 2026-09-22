using System.Globalization;
using System.Net;
using System.Text.Json;

namespace ToolsBox.MediaDownloads;

public sealed partial class MediaDownloadService
{
    private const long CapturedVideoSizeLimit = 10L * 1024 * 1024 * 1024;
    // A captured response is a complete file, never a playlist that may request other URLs/files.
    private const string CapturedContainerFormats = "mov,matroska,webm,avi,flv,mpegts,mpeg,ogg,asf";

    public async Task<string> DownloadCapturedVideoAsync(Uri uri, string outputDirectory, string title,
        double? expectedDurationSeconds = null, IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default, IReadOnlyList<MediaCookie>? cookies = null,
        string? referer = null, string? userAgent = null)
    {
        ValidateCapturedUri(uri);
        if (expectedDurationSeconds is { } duration && (!double.IsFinite(duration) || duration <= 0))
            throw new ArgumentException("视频预期时长无效。", nameof(expectedDurationSeconds));
        var origin = RefererOrigin(referer);
        var agent = SafeUserAgent(userAgent);
        ValidateCapturedCookies(cookies);
        var paths = await components.GetInstalledAsync(ct) ?? throw new MediaDownloadException(MediaDownloadError.ComponentsMissing);
        Directory.CreateDirectory(outputDirectory);
        var stage = Path.Combine(outputDirectory, ".video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var source = Path.Combine(stage, "source.media");
            await TransferCapturedVideo(uri, source, cookies, origin, agent, progress, ct);
            await ConversionGate.WaitAsync(ct);
            try
            {
                progress?.Report(new("验证源视频"));
                var sourceProbe = await ProcessRunner.RunAsync(paths.FFprobe,
                    ["-v", "error", "-protocol_whitelist", "file,pipe", "-format_whitelist", CapturedContainerFormats,
                     "-show_entries", "format=format_name,duration:stream=codec_type,codec_name", "-of", "json", source], ct);
                EnsureSuccess(sourceProbe, "源视频验证失败。");
                var info = CapturedSourceInfo(sourceProbe.Output, title, expectedDurationSeconds);
                var output = Path.Combine(stage, "result.mp4");
                var common = new[] { "-nostdin", "-v", "error", "-n", "-protocol_whitelist", "file,pipe",
                    "-format_whitelist", CapturedContainerFormats, "-i", source, "-map", "0:v:0", "-map", "0:a?" };
                progress?.Report(new("封装 MP4"));
                var remux = VideoInfo.CanRemuxToMp4(sourceProbe.Output)
                    ? await ProcessRunner.RunAsync(paths.FFmpeg, common.Concat(["-c", "copy", "-movflags", "+faststart", output]), ct)
                    : new ProcessResult(1, "", "");
                if (remux.ExitCode != 0)
                {
                    if (File.Exists(output)) File.Delete(output);
                    progress?.Report(new("转换 MP4"));
                    EnsureSuccess(await ProcessRunner.RunAsync(paths.FFmpeg,
                        common.Concat(["-c:v", "libx264", "-preset", "medium", "-crf", "20", "-c:a", "aac", "-movflags", "+faststart", output]), ct), "MP4 转换失败。");
                }
                progress?.Report(new("验证 MP4"));
                var probe = await ProcessRunner.RunAsync(paths.FFprobe,
                    ["-v", "error", "-protocol_whitelist", "file,pipe", "-format_whitelist", "mov",
                     "-show_entries", "format=format_name,duration:stream=codec_type", "-of", "json", output], ct);
                EnsureSuccess(probe, "MP4 验证失败。");
                try { info.ValidateOutput(probe.Output); }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException)
                { throw new MediaDownloadException(MediaDownloadError.VerificationFailed); }
                ct.ThrowIfCancellationRequested();
                var final = Publish(output, outputDirectory, title, ".mp4");
                progress?.Report(new("完成", 100));
                return final;
            }
            finally { ConversionGate.Release(); }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new MediaDownloadException(MediaDownloadError.DownloadFailed); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { throw new MediaDownloadException(MediaDownloadError.DownloadFailed); }
        finally
        {
            // Only this invocation's randomly named staging directory is ever removed.
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task TransferCapturedVideo(Uri uri, string destination, IReadOnlyList<MediaCookie>? cookies,
        Uri? referer, string? userAgent, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        // Do not let HttpClient forward a manually attached Cookie header during automatic redirects.
        // An injected client is a trusted transport supplied by the host/tests; production owns this handler.
        using var owned = httpClient is null ? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false,
            AutomaticDecompression = DecompressionMethods.None
        }) { Timeout = Timeout.InfiniteTimeSpan } : null;
        var client = httpClient ?? owned!;
        var current = uri;
        for (var redirect = 0; redirect <= 10; redirect++)
        {
            ct.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Referrer = referer;
            if (userAgent != null) request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            var header = CapturedCookieHeader(cookies, current);
            if (header.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", header);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            // Reject a transport that silently followed redirects instead of returning them for validation.
            if (response.RequestMessage?.RequestUri is { } actual && actual != current)
                throw new MediaDownloadException(MediaDownloadError.DownloadFailed);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                if (redirect == 10 || location == null || !Uri.TryCreate(current, location, out var next))
                    throw new MediaDownloadException(MediaDownloadError.DownloadFailed);
                try { ValidateCapturedUri(next); }
                catch (ArgumentException) { throw new MediaDownloadException(MediaDownloadError.DownloadFailed); }
                if (current.Scheme == "https" && next.Scheme != "https")
                    throw new MediaDownloadException(MediaDownloadError.DownloadFailed);
                current = next;
                continue;
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new MediaDownloadException(MediaDownloadError.LoginRequired);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new MediaDownloadException(MediaDownloadError.RateLimited);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new MediaDownloadException(MediaDownloadError.DownloadFailed);
            var length = response.Content.Headers.ContentLength;
            if (length is > CapturedVideoSizeLimit or 0)
                throw new MediaDownloadException(MediaDownloadError.DownloadFailed);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is "text/html" or "application/json" or "application/vnd.apple.mpegurl" or "application/x-mpegURL" or "application/dash+xml")
                throw new MediaDownloadException(MediaDownloadError.VerificationFailed);
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
            var buffer = new byte[65536];
            long size = 0;
            progress?.Report(new("下载播放器视频"));
            while (true)
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                var read = await input.ReadAsync(buffer, timeout.Token);
                if (read == 0) break;
                size += read;
                if (size > CapturedVideoSizeLimit) throw new MediaDownloadException(MediaDownloadError.DownloadFailed);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                progress?.Report(new("下载播放器视频", length is > 0 ? Math.Clamp(100d * size / length.Value, 0, 100) : null));
            }
            ct.ThrowIfCancellationRequested();
            if (size == 0 || (length is { } expected && size != expected))
                throw new MediaDownloadException(MediaDownloadError.DownloadFailed);
            return;
        }
        throw new MediaDownloadException(MediaDownloadError.DownloadFailed);
    }

    private static void ValidateCapturedUri(Uri uri)
    {
        ValidateUri(uri);
        if (uri.OriginalString.Any(char.IsControl)) throw new ArgumentException("视频地址格式无效。");
    }

    private static void ValidateCapturedCookies(IReadOnlyList<MediaCookie>? cookies)
    {
        if (cookies == null) return;
        if (cookies.Count > 2000) throw new ArgumentException("Cookie 数量超过限制。");
        foreach (var c in cookies)
        {
            if (c.Name == null || c.Value == null || c.Domain == null || c.Path == null ||
                (c.Name.Length == 0 && c.Value.Length == 0) ||
                c.Name.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || "!#$%&'*+-.^_`|~".Contains(ch))) ||
                c.Value.Any(ch => char.IsControl(ch) || ch == ';') ||
                c.Domain.Any(char.IsControl) || c.Path.Any(char.IsControl) || !c.Path.StartsWith('/') ||
                Uri.CheckHostName(c.Domain.TrimStart('.')) == UriHostNameType.Unknown)
                throw new ArgumentException("Cookie 格式无效。");
        }
    }

    private static string CapturedCookieHeader(IReadOnlyList<MediaCookie>? cookies, Uri uri)
    {
        if (cookies == null) return "";
        var now = DateTimeOffset.UtcNow;
        var values = new List<string>();
        foreach (var c in cookies.OrderByDescending(c => c.Path.Length))
        {
            if (c.Secure && uri.Scheme != "https" || c.Expires <= now) continue;
            var domain = c.Domain.TrimStart('.');
            var sameHost = string.Equals(uri.IdnHost, domain, StringComparison.OrdinalIgnoreCase);
            if (!sameHost && (c.HostOnly || Uri.CheckHostName(domain) != UriHostNameType.Dns ||
                !uri.IdnHost.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))) continue;
            var path = uri.AbsolutePath;
            if (!path.StartsWith(c.Path, StringComparison.Ordinal) ||
                (path.Length != c.Path.Length && !c.Path.EndsWith('/') && path[c.Path.Length] != '/')) continue;
            values.Add(c.Name.Length == 0 ? c.Value : c.Name + "=" + c.Value);
        }
        var header = string.Join("; ", values);
        if (header.Length > 65536) throw new ArgumentException("Cookie 请求头超过限制。");
        return header;
    }

    private static VideoInfo CapturedSourceInfo(string probeJson, string title, double? expectedDuration)
    {
        try
        {
            using var document = JsonDocument.Parse(probeJson);
            var root = document.RootElement;
            var streams = root.GetProperty("streams").EnumerateArray().ToArray();
            if (!streams.Any(s => s.GetProperty("codec_type").GetString() == "video"))
                throw new InvalidDataException();
            var duration = root.GetProperty("format").GetProperty("duration").ToString();
            if (!double.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                !double.IsFinite(seconds) || seconds <= 0 ||
                (expectedDuration is { } expected && Math.Abs(seconds - expected) > Math.Max(2, expected * 0.02)))
                throw new InvalidDataException();
            return new(title, [], expectedDuration ?? seconds,
                streams.Any(s => s.GetProperty("codec_type").GetString() == "audio"));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
        { throw new MediaDownloadException(MediaDownloadError.VerificationFailed); }
    }
}
