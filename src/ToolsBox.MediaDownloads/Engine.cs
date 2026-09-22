using System.Net;
using System.Text.Json;
namespace ToolsBox.MediaDownloads;

public record DownloadProgress(string Status, double? Percent = null, string? Detail = null);
public record ComponentPaths(string YtDlp, string FFmpeg, string FFprobe);
public record VideoFormat(string Id, string Label, int? Height, string Extension);
public record VideoInfo(string Title, IReadOnlyList<VideoFormat> Formats, double? DurationSeconds = null, bool HasAudio = false)
{
    internal static void ValidateExpectedDurationArgument(double? expectedDurationSeconds)
    {
        if (expectedDurationSeconds is { } expected &&
            (!double.IsFinite(expected) || expected <= 0 || expected > TimeSpan.MaxValue.TotalSeconds / 2))
            throw new ArgumentException("视频预期时长无效。", nameof(expectedDurationSeconds));
    }
    public void ValidateExpectedDuration(double? expectedDurationSeconds)
    {
        ValidateExpectedDurationArgument(expectedDurationSeconds);
        if (expectedDurationSeconds is not { } expected) return;
        if (DurationSeconds is not { } actual || !double.IsFinite(actual) || actual <= 0 ||
            actual > TimeSpan.MaxValue.TotalSeconds / 2 || Math.Abs(actual - expected) > Math.Max(2, expected * 0.02))
            throw new InvalidDataException("无法确认解析视频与当前播放器的时长一致，可能是广告或其他分集，已停止下载。");
    }
    public static bool CanRemuxToMp4(string probeJson)
    {
        using var document = JsonDocument.Parse(probeJson);
        if (!document.RootElement.TryGetProperty("streams", out var streams)) return false;
        var video = false;
        foreach (var stream in streams.EnumerateArray())
        {
            var type = stream.GetProperty("codec_type").GetString();
            var codec = stream.GetProperty("codec_name").GetString();
            if (type == "video") { if (codec is not ("h264" or "hevc" or "av1")) return false; video = true; }
            if (type == "audio" && codec is not ("aac" or "mp3" or "alac")) return false;
        }
        return video;
    }
    public void ValidateOutput(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var format) || !format.TryGetProperty("format_name", out var name) || !((name.GetString() ?? "").Split(',').Contains("mp4")) || !root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array) throw new InvalidDataException("输出不是有效 MP4 视频。");
        var kinds = streams.EnumerateArray().Select(s => s.TryGetProperty("codec_type", out var kind) ? kind.GetString() : null).ToArray();
        if (!kinds.Contains("video") || (HasAudio && !kinds.Contains("audio"))) throw new InvalidDataException("输出缺少预期的视频或音频轨道。");
        if (!format.TryGetProperty("duration", out var d) || !double.TryParse(d.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var actual) || !double.IsFinite(actual) || actual <= 0) throw new InvalidDataException("输出时长无效。");
        if (DurationSeconds is > 0 && double.IsFinite(DurationSeconds.Value) && Math.Abs(actual - DurationSeconds.Value) > Math.Max(2, DurationSeconds.Value * 0.02)) throw new InvalidDataException("输出时长与原视频不符，可能下载不完整。");
    }
    public static VideoInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json); var r = doc.RootElement;
        if (r.TryGetProperty("entries", out _) || (r.TryGetProperty("_type", out var t) && t.GetString() is "playlist" or "multi_video")) throw new InvalidDataException("仅支持单个视频。");
        var formats = new List<VideoFormat>(); var audio = r.TryGetProperty("acodec", out var ac) && ac.GetString() is not (null or "none");
        if (r.TryGetProperty("formats", out var fs)) foreach (var f in fs.EnumerateArray())
        {
            if (f.TryGetProperty("has_drm", out var drm) && drm.ValueKind == JsonValueKind.True) continue;
            if (f.TryGetProperty("acodec", out var codec) && codec.GetString() is not (null or "none")) audio = true;
            if (!f.TryGetProperty("vcodec", out var vc) || vc.GetString() is null or "none") continue;
            var id = f.GetProperty("format_id").GetString()!; int? h = f.TryGetProperty("height", out var ht) && ht.ValueKind == JsonValueKind.Number && ht.TryGetInt32(out var n) ? n : null;
            var ext = f.TryGetProperty("ext", out var ex) ? ex.GetString() ?? "" : "";
            formats.Add(new(id, $"{h?.ToString() ?? "未知"}p · {ext} · {id}", h, ext));
        }
        double? duration = r.TryGetProperty("duration", out var ds) && ds.ValueKind == JsonValueKind.Number && ds.TryGetDouble(out var seconds) && double.IsFinite(seconds) && seconds > 0 ? seconds : null;
        return new(r.TryGetProperty("title", out var title) ? title.GetString() ?? "视频" : "视频", formats.OrderByDescending(f => f.Height).ToArray(), duration, audio);
    }
}
public sealed partial class ComponentManager(string rootDirectory, HttpClient? downloadClient = null)
{
    public async Task<ComponentPaths> ImportAsync(string trustedDirectory, CancellationToken ct = default)
    {
        var source = Paths(Path.GetFullPath(trustedDirectory));
        foreach (var path in new[] { source.YtDlp, source.FFmpeg, source.FFprobe }) if (!File.Exists(path)) throw new FileNotFoundException("目录必须包含 yt-dlp.exe、ffmpeg.exe 和 ffprobe.exe。", path);
        var stage = NewStage(); try { foreach (var path in new[] { source.YtDlp, source.FFmpeg, source.FFprobe }) File.Copy(path, Path.Combine(stage, Path.GetFileName(path)), false); await ValidateAsync(Paths(stage), ct); return Activate(stage); } catch { Directory.Delete(stage, true); throw; }
    }
    public Task<ComponentPaths?> GetInstalledAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); var marker = Path.Combine(rootDirectory, "active.txt");
        if (!File.Exists(marker)) return Task.FromResult<ComponentPaths?>(null);
        var name = File.ReadAllText(marker).Trim(); if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-f0-9]{32}$")) return Task.FromResult<ComponentPaths?>(null);
        var p = Paths(Path.Combine(rootDirectory, name)); return Task.FromResult<ComponentPaths?>(File.Exists(p.YtDlp) && File.Exists(p.FFmpeg) && File.Exists(p.FFprobe) ? p : null);
    }
    private static ComponentPaths Paths(string dir) => new(Path.Combine(dir, "yt-dlp.exe"), Path.Combine(dir, "ffmpeg.exe"), Path.Combine(dir, "ffprobe.exe"));
}
public sealed partial class MediaDownloadService(ComponentManager components, HttpClient? httpClient = null)
{
    public async Task<string> DownloadImageAsync(Uri uri, string outputDirectory, string? suggestedName = null, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default, CookieContainer? cookies = null, string? referer = null, string? userAgent = null)
    {
        ValidateUri(uri); Directory.CreateDirectory(outputDirectory); var temp = Path.Combine(outputDirectory, ".image-" + Guid.NewGuid().ToString("N") + ".part");
        using var owned = httpClient is null ? new HttpClient(new HttpClientHandler { UseCookies = cookies != null, CookieContainer = cookies ?? new(), AllowAutoRedirect = true }) : null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Referrer = RefererOrigin(referer);
            var agent = SafeUserAgent(userAgent);
            if (agent != null) request.Headers.TryAddWithoutValidation("User-Agent", agent);
            using var response = await (httpClient ?? owned!).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(ct)) await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[65536]; long size = 0; int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0) { size += read; if (size > 100 * 1024 * 1024) throw new InvalidDataException("图片超过 100 MB 限制。"); await output.WriteAsync(buffer.AsMemory(0, read), ct); progress?.Report(new("下载图片", response.Content.Headers.ContentLength is > 0 ? 100d * size / response.Content.Headers.ContentLength : null)); }
            }
            var bytes = await File.ReadAllBytesAsync(temp, ct); var ext = ImageExtension(bytes);
            var path = Publish(temp, outputDirectory, suggestedName ?? Path.GetFileNameWithoutExtension(uri.AbsolutePath), ext); progress?.Report(new("完成", 100)); return path;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static void ValidateUri(Uri uri) { if (!uri.IsAbsoluteUri || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("仅支持不含凭据的 HTTP(S) 地址。"); }
    internal static string ImageExtension(byte[] b)
    {
        if (b.Length >= 45 && b.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && b.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            var offset = 8; var data = false;
            while (offset <= b.Length - 12) { var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(offset, 4)); if (size > (uint)(b.Length - offset - 12)) break; var kind = b.AsSpan(offset + 4, 4); if (kind.SequenceEqual("IDAT"u8)) data = true; if (kind.SequenceEqual("IEND"u8) && size == 0 && data && offset + 12 == b.Length) return ".png"; offset += checked((int)size + 12); }
        }
        if (IsJpeg(b)) return ".jpg";
        if (b.Length >= 14 && (b.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || b.AsSpan(0, 6).SequenceEqual("GIF89a"u8)) && b[^1] == 59) return ".gif";
        if (IsWebP(b)) return ".webp";
        throw new InvalidDataException("响应不是支持的 PNG/JPEG/GIF/WebP 图片。");
    }
    internal static string Publish(string source, string directory, string? name, string extension)
    {
        var safe = new string((name ?? "下载").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim(' ', '.'); if (safe.Length == 0) safe = "下载"; if (safe.Length > 100) safe = safe[..100];
        if (System.Text.RegularExpressions.Regex.IsMatch(safe, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) safe = "_" + safe;
        for (int i = 0; i < 10000; i++) { var target = Path.Combine(directory, safe + (i == 0 ? "" : $" ({i})") + extension); try { File.Move(source, target, false); return target; } catch (IOException) when (File.Exists(target)) { } }
        throw new IOException("无法分配输出文件名。");
    }
}
