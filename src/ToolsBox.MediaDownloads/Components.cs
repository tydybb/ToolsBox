using System.IO.Compression;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
namespace ToolsBox.MediaDownloads;

public sealed partial class ComponentManager
{
    public const string YtDlpVersion = "2026.08.19";
    public const string FFmpegVersion = "9.0.1";
    public const string YtDlpSource = "https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/yt-dlp.exe";
    public const string FFmpegSource = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.1-essentials_build.zip";
    public async Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        var paths = await GetInstalledAsync(ct);
        if (paths == null) return false;
        try { await ValidateAsync(paths, ct); return true; }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
    }
    public async Task<string> DescribeAsync(CancellationToken ct = default)
    {
        var paths = await GetInstalledAsync(ct);
        if (paths == null) return "未安装视频组件。\n" + LockedSources();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var lines = new List<string>();
        foreach (var (file, argument, label) in new[] { (paths.YtDlp, "--version", "yt-dlp"), (paths.FFmpeg, "-version", "FFmpeg"), (paths.FFprobe, "-version", "ffprobe") })
        {
            var result = await ProcessRunner.RunAsync(file, new[] { argument }, timeout.Token);
            var match = Regex.Match(result.Output, @"(?m)^(?:ffmpeg version |ffprobe version )?([0-9][A-Za-z0-9._+-]{0,80})(?:\s|$)");
            lines.Add(label + ": " + (result.ExitCode == 0 && match.Success ? match.Groups[1].Value : "版本检查失败"));
        }
        lines.Add(LockedSources());
        lines.Add("组件目录：" + Path.GetDirectoryName(paths.YtDlp));
        lines.Add("安装包校验：固定版本官方 HTTPS SHA256；许可与来源记录保存在组件目录。手动导入组件由用户提供。 ");
        return string.Join(Environment.NewLine, lines);
    }
    private static string LockedSources() => "固定下载版本：yt-dlp " + YtDlpVersion + " / FFmpeg " + FFmpegVersion + "\n" + YtDlpSource + "\n" + FFmpegSource;
    private string NewStage() { Directory.CreateDirectory(rootDirectory); var path = Path.Combine(rootDirectory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private ComponentPaths Activate(string stage)
    {
        var temp = Path.Combine(rootDirectory, Guid.NewGuid().ToString("N") + ".tmp"); File.WriteAllText(temp, Path.GetFileName(stage)); File.Move(temp, Path.Combine(rootDirectory, "active.txt"), true); return Paths(stage);
    }
    private static async Task ValidateAsync(ComponentPaths p, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        foreach (var (path, arg, token) in new[] { (p.YtDlp, "--version", ""), (p.FFmpeg, "-version", "ffmpeg version"), (p.FFprobe, "-version", "ffprobe version") })
        {
            using (var binary = File.OpenRead(path))
            using (var pe = new PEReader(binary))
                if (pe.PEHeaders.PEHeader == null) throw new InvalidDataException("组件不是有效 Windows 程序。");
            var r = await ProcessRunner.RunAsync(path, new[] { arg }, timeout.Token); if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.Output) || !r.Output.Contains(token, StringComparison.OrdinalIgnoreCase) || (token == "" && !Regex.IsMatch(r.Output.Trim(), @"^\d{4}\.\d{2}\.\d{2}"))) throw new InvalidDataException("组件版本检查失败。");
        }
    }
    public async Task<ComponentPaths> InstallAsync(IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var stage = NewStage(); using var owned = downloadClient is null ? new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromMinutes(15) } : null; var client = downloadClient ?? owned!;
        try
        {
            progress?.Report(new("下载并校验 yt-dlp"));
            var sums = await client.GetStringAsync("https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/SHA2-256SUMS", ct);
            var match = Regex.Match(sums, @"(?m)^([a-fA-F0-9]{64})\s+\*?yt-dlp\.exe\s*$"); if (!match.Success) throw new InvalidDataException("未找到 yt-dlp 官方校验值。");
            await FetchVerified(client, YtDlpSource, Path.Combine(stage, "yt-dlp.exe"), match.Groups[1].Value, progress, "yt-dlp", ct);
            progress?.Report(new("下载并校验 FFmpeg")); var hash = (await client.GetStringAsync(FFmpegSource + ".sha256", ct)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
            var zip = Path.Combine(stage, "ffmpeg.zip"); await FetchVerified(client, FFmpegSource, zip, hash, progress, "FFmpeg", ct);
            progress?.Report(new("解压视频组件"));
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var exe in new[] { "ffmpeg.exe", "ffprobe.exe" })
                {
                    var entry = archive.Entries.SingleOrDefault(e => e.FullName.EndsWith("/bin/" + exe, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("FFmpeg 包缺少程序。");
                    if (entry.Length > 300 * 1024 * 1024) throw new InvalidDataException("组件文件过大。"); entry.ExtractToFile(Path.Combine(stage, exe), false);
                }
                var notice = archive.Entries.FirstOrDefault(e => e.Name.Equals("LICENSE", StringComparison.OrdinalIgnoreCase) || e.Name.Equals("LICENSE.txt", StringComparison.OrdinalIgnoreCase));
                if (notice == null || notice.Length > 1024 * 1024) throw new InvalidDataException("FFmpeg 包缺少许可文件。");
                notice.ExtractToFile(Path.Combine(stage, "FFmpeg-LICENSE.txt"), false);
            }
            var ytLicense = await client.GetStringAsync("https://raw.githubusercontent.com/yt-dlp/yt-dlp/2026.08.19/LICENSE", ct);
            var thirdParty = await client.GetStringAsync("https://raw.githubusercontent.com/yt-dlp/yt-dlp/2026.08.19/THIRD_PARTY_LICENSES.txt", ct);
            await File.WriteAllTextAsync(Path.Combine(stage, "yt-dlp-LICENSE.txt"), ytLicense, ct);
            await File.WriteAllTextAsync(Path.Combine(stage, "yt-dlp-THIRD_PARTY_LICENSES.txt"), thirdParty, ct);
            await File.WriteAllTextAsync(Path.Combine(stage, "PROVENANCE.txt"), LockedSources() + "\nyt-dlp SHA256: " + match.Groups[1].Value + "\nFFmpeg archive SHA256: " + hash + "\nChecksum sources: corresponding versioned official HTTPS release metadata.\n", ct);
            File.Delete(zip); progress?.Report(new("检查视频组件版本")); await ValidateAsync(Paths(stage), ct); var paths = Activate(stage); progress?.Report(new("组件已就绪", 100)); return paths;
        }
        catch { Directory.Delete(stage, true); throw; }
    }
    private static async Task FetchVerified(HttpClient client, string url, string path, string hash, IProgress<DownloadProgress>? progress, string label, CancellationToken ct)
    {
        if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("无效 SHA256。");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new InvalidDataException("组件下载必须使用 HTTPS。");
        var clock = Stopwatch.StartNew(); long lastReport = -100;
        await using (var input = await response.Content.ReadAsStreamAsync(ct)) await using (var output = File.Create(path))
        {
            var buffer = new byte[65536]; long count = 0; int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                count += read;
                if (count > 500 * 1024 * 1024) throw new InvalidDataException("组件下载超过限制。");
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                if (clock.ElapsedMilliseconds - lastReport >= 100 || count == response.Content.Headers.ContentLength)
                {
                    lastReport = clock.ElapsedMilliseconds;
                    progress?.Report(new("下载 " + label, response.Content.Headers.ContentLength is > 0 ? Math.Min(100, 100d * count / response.Content.Headers.ContentLength.Value) : null, $"已下载 {count / 1048576d:F1} MB"));
                }
            }
        }
        progress?.Report(new("校验 " + label));
        await using var file = File.OpenRead(path); var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct)); if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("组件 SHA256 校验失败，未激活。");
    }
}
