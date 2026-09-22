using System.Net;
using ToolsBox.MediaDownloads;

namespace ToolsBox.MediaDownloads.Tests;

public class ComponentProgressTests
{
    [Fact]
    public async Task ExistingButInvalidProgramsAreNotReady()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var stage = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(dir, stage));
        try
        {
            File.WriteAllText(Path.Combine(dir, "active.txt"), stage);
            // A real harmless Windows executable exits promptly but is not a valid media component.
            foreach (var name in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe" }) File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe"), Path.Combine(dir, stage, name));
            var manager = new ComponentManager(dir);
            Assert.NotNull(await manager.GetInstalledAsync());
            Assert.False(await manager.IsReadyAsync());
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task CorruptFilesAreRejectedBeforeWindowsTriesToLaunchThem()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var stage = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(dir, stage));
        try
        {
            File.WriteAllText(Path.Combine(dir, "active.txt"), stage);
            foreach (var name in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe" }) File.WriteAllText(Path.Combine(dir, stage, name), "invalid executable");
            Assert.False(await new ComponentManager(dir).IsReadyAsync());
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task DownloadReportsBytePercentBeforeChecksumFailure()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var reports = new List<DownloadProgress>();
        try
        {
            using var client = new HttpClient(new Reply());
            await Assert.ThrowsAsync<InvalidDataException>(() => new ComponentManager(dir, client).InstallAsync(new Capture(reports)));
            Assert.Contains(reports, p => p.Percent is > 0 and < 100);
            Assert.Contains(reports, p => p.Status.Contains("校验") && p.Percent == null);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    private sealed class Capture(List<DownloadProgress> items) : IProgress<DownloadProgress>
    { public void Report(DownloadProgress value) => items.Add(value); }
    private sealed class Reply : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = request.RequestUri!.AbsolutePath.EndsWith("SHA2-256SUMS")
                    ? new StringContent(new string('0', 64) + "  yt-dlp.exe")
                    : new ByteArrayContent(new byte[200000])
            });
    }
}
