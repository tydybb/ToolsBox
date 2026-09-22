using System.Text.Json;
using System.Text.RegularExpressions;
namespace ToolsBox.MediaDownloads;

public record MediaCookie(string Name, string Value, string Domain, string Path, bool Secure, DateTimeOffset? Expires, bool HostOnly = true);
public sealed partial class MediaDownloadService
{
    private static readonly SemaphoreSlim ConversionGate = new(1, 1);
    private static List<string> BaseArguments() => new() { "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--no-warnings", "--no-check-formats", "--socket-timeout", "30", "--retries", "3" };
    public async Task<VideoInfo> InspectVideoAsync(Uri uri, CancellationToken ct = default, IReadOnlyList<MediaCookie>? cookies = null, string? referer = null, string? userAgent = null)
    {
        ValidateUri(uri); var paths = await components.GetInstalledAsync(ct) ?? throw new MediaDownloadException(MediaDownloadError.ComponentsMissing);
        using var cookieFile = CookieFile.Create(cookies); var args = BaseArguments(); if (cookieFile != null) args.AddRange(new[] { "--cookies", cookieFile.Path });
        AddRequestContext(args, referer, userAgent);
        args.AddRange(new[] { "--dump-single-json", "--skip-download", "--", uri.AbsoluteUri });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var result = await ProcessRunner.RunAsync(paths.YtDlp, args, timeout.Token); EnsureSuccess(result, "视频解析失败：链接不支持、需要登录或受保护。"); return VideoInfo.Parse(result.Output);
    }
    public async Task<string> DownloadVideoAsync(Uri uri, string outputDirectory, string? formatId = null, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default, IReadOnlyList<MediaCookie>? cookies = null, string? referer = null, string? userAgent = null, double? expectedDurationSeconds = null)
    {
        ValidateUri(uri); if (formatId != null && !Regex.IsMatch(formatId, "^[a-zA-Z0-9_.-]{1,100}$")) throw new ArgumentException("无效的视频格式编号。");
        VideoInfo.ValidateExpectedDurationArgument(expectedDurationSeconds);
        var paths = await components.GetInstalledAsync(ct) ?? throw new MediaDownloadException(MediaDownloadError.ComponentsMissing);
        var info = await InspectVideoAsync(uri, ct, cookies, referer, userAgent);
        info.ValidateExpectedDuration(expectedDurationSeconds);
        if (formatId != null && !info.Formats.Any(f => f.Id == formatId)) throw new ArgumentException("所选视频格式已不可用，请重新解析。");
        Directory.CreateDirectory(outputDirectory); var stage = Path.Combine(outputDirectory, ".video-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            using var cookieFile = CookieFile.Create(cookies); var args = BaseArguments(); if (cookieFile != null) args.AddRange(new[] { "--cookies", cookieFile.Path });
            AddRequestContext(args, referer, userAgent);
            args.AddRange(new[] { "--newline", "--progress", "--progress-template", "download:PROGRESS %(progress._percent_str)s %(progress._speed_str)s", "--no-overwrites", "--max-filesize", "10G", "--ffmpeg-location", Path.GetDirectoryName(paths.FFmpeg)!, "--merge-output-format", "mkv", "-f", formatId == null ? "bv*+ba/b" : $"{formatId}+ba/{formatId}", "-o", Path.Combine(stage, "source.%(ext)s"), "--", uri.AbsoluteUri });
            progress?.Report(new("下载视频")); EnsureSuccess(await ProcessRunner.RunAsync(paths.YtDlp, args, ct, line => { var m = Regex.Match(line, @"^PROGRESS\s+([\d.]+)%\s+([\d.]+[KMG]?i?B/s)"); if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent)) progress?.Report(new("下载视频", Math.Clamp(percent, 0, 100), m.Groups[2].Value)); }), "视频下载失败：链接失效、需要登录或不支持。");
            var source = Directory.GetFiles(stage, "source.*").SingleOrDefault(p => !p.EndsWith(".part") && !p.EndsWith(".ytdl")) ?? throw new InvalidDataException("组件未生成完整视频。");
            await ConversionGate.WaitAsync(ct);
            try
            {
                var output = Path.Combine(stage, "result.mp4"); progress?.Report(new("封装 MP4"));
                var sourceProbe = await ProcessRunner.RunAsync(paths.FFprobe, new[] { "-v", "error", "-show_entries", "stream=codec_type,codec_name", "-of", "json", source }, ct);
                EnsureSuccess(sourceProbe, "源视频验证失败。");
                var remux = VideoInfo.CanRemuxToMp4(sourceProbe.Output)
                    ? await ProcessRunner.RunAsync(paths.FFmpeg, new[] { "-nostdin", "-v", "error", "-n", "-i", source, "-map", "0:v:0", "-map", "0:a?", "-c", "copy", "-movflags", "+faststart", output }, ct)
                    : new ProcessResult(1, "", "");
                if (remux.ExitCode != 0) { if (File.Exists(output)) File.Delete(output); progress?.Report(new("转换 MP4")); EnsureSuccess(await ProcessRunner.RunAsync(paths.FFmpeg, new[] { "-nostdin", "-v", "error", "-n", "-i", source, "-map", "0:v:0", "-map", "0:a?", "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-c:a", "aac", "-movflags", "+faststart", output }, ct), "MP4 转换失败。"); }
                var probe = await ProcessRunner.RunAsync(paths.FFprobe, new[] { "-v", "error", "-show_entries", "format=format_name,duration:stream=codec_type", "-of", "json", output }, ct); EnsureSuccess(probe, "MP4 验证失败。");
                try { info.ValidateOutput(probe.Output); }
                catch (Exception ex) when (ex is InvalidDataException or JsonException) { throw new MediaDownloadException(MediaDownloadError.VerificationFailed); }
                var final = Publish(output, outputDirectory, info.Title, ".mp4"); progress?.Report(new("完成", 100)); return final;
            }
            finally { ConversionGate.Release(); }
        }
        finally { Directory.Delete(stage, true); }
    }
    private static void EnsureSuccess(ProcessResult result, string message)
    {
        if (result.ExitCode == 0) return;
        var code = message.Contains("解析") ? MediaDownloadError.ParseFailed : message.Contains("转换") ? MediaDownloadError.ConversionFailed : message.Contains("验证") ? MediaDownloadError.VerificationFailed : MediaDownloadError.DownloadFailed;
        throw MediaDownloadException.FromProcess(code, result.Error);
    }
}
