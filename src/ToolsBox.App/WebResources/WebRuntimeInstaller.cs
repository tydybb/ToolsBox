using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using ToolsBox.MediaDownloads;

namespace ToolsBox.App.WebResources;

public interface IWebRuntimeBackend
{
    string? DetectVersion();
    Task DownloadAsync(string path, IProgress<DownloadProgress>? progress, CancellationToken ct);
    Task<bool> VerifyMicrosoftSignatureAsync(string path, CancellationToken ct);
    // Once started, an installer must be allowed to finish; cancellation is deliberately absent.
    Task<int> InstallAsync(string path);
}

public sealed class WebRuntimeInstaller(IWebRuntimeBackend? backend = null)
{
    public const string Source = "https://go.microsoft.com/fwlink/?linkid=2124701";
    private readonly IWebRuntimeBackend backend = backend ?? new WindowsWebRuntimeBackend();
    public bool IsInstalling { get; private set; }
    public string? DetectVersion() => backend.DetectVersion();

    public async Task<string> InstallAsync(IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var installed = DetectVersion();
        if (installed != null) return installed;
        var stage = Path.Combine(Path.GetTempPath(), "ToolsBox-WebView2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var path = Path.Combine(stage, "MicrosoftEdgeWebView2RuntimeInstallerX64.exe");
            await backend.DownloadAsync(path, progress, ct);
            progress?.Report(new("校验 Microsoft 数字签名"));
            if (!await backend.VerifyMicrosoftSignatureAsync(path, ct))
                throw new InvalidDataException("安装包签名无效或发布者不是 Microsoft，已停止安装。请重试。");
            ct.ThrowIfCancellationRequested();
            IsInstalling = true;
            progress?.Report(new("正在安装 WebView2，请等待安装程序完成"));
            var code = await backend.InstallAsync(path);
            if (code is not (0 or 3010 or 1641))
                throw new InvalidOperationException($"WebView2 安装失败（退出码 {code}），可重试。");
            progress?.Report(new("检查 WebView2 安装结果"));
            return DetectVersion() ?? throw new InvalidOperationException("安装程序已退出，但仍未检测到 WebView2。请重试或重新启动工具箱。");
        }
        finally
        {
            IsInstalling = false;
            try { Directory.Delete(stage, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

public sealed class WindowsWebRuntimeBackend(Func<string?>? registeredVersion = null, Func<string?>? availableVersion = null) : IWebRuntimeBackend
{
    public string? DetectVersion()
    {
        if ((registeredVersion ?? ReadRegisteredVersion)() == null) return null;
        try
        {
            var available = (availableVersion ?? ReadAvailableVersion)();
            return Version.TryParse(available, out var version) && version > new Version(0, 0, 0, 0) ? available : null;
        }
        catch (WebView2RuntimeNotFoundException) { return null; }
    }

    private static string? ReadAvailableVersion() => CoreWebView2Environment.GetAvailableBrowserVersionString(null,
        new CoreWebView2EnvironmentOptions { ReleaseChannels = CoreWebView2ReleaseChannels.Stable });

    private static string? ReadRegisteredVersion()
    {
        // Registry detection deliberately excludes Edge preview channels and does not access the network.
        const string key = @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry32);
            using var client = root.OpenSubKey(key);
            if (client?.GetValue("pv") is string text && Version.TryParse(text, out var version) && version > new Version(0, 0, 0, 0)) return text;
        }
        return null;
    }

    public async Task DownloadAsync(string path, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (!Environment.Is64BitOperatingSystem) throw new PlatformNotSupportedException("当前安装包需要 64 位 Windows。");
        if (WebResourceLauncher.IsAdministrator()) throw new InvalidOperationException("请在普通权限的网页资源窗口安装 WebView2。");
        using var client = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromMinutes(20) };
        progress?.Report(new("连接 Microsoft 下载服务"));
        using var response = await client.GetAsync(WebRuntimeInstaller.Source, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new InvalidDataException("WebView2 下载必须使用 HTTPS。");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        var buffer = new byte[65536]; long count = 0; int read;
        var clock = Stopwatch.StartNew(); long lastReport = -100;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            count += read;
            if (count > 500L * 1024 * 1024) throw new InvalidDataException("WebView2 安装包超过 500 MB 下载限制。");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            if (clock.ElapsedMilliseconds - lastReport >= 100 || count == response.Content.Headers.ContentLength)
            {
                lastReport = clock.ElapsedMilliseconds;
                progress?.Report(new("下载 WebView2", response.Content.Headers.ContentLength is > 0 ? Math.Min(100, 100d * count / response.Content.Headers.ContentLength.Value) : null, $"已下载 {count / 1048576d:F1} MB"));
            }
        }
    }

    public async Task<bool> VerifyMicrosoftSignatureAsync(string path, CancellationToken ct)
    {
        // Windows Authenticode verifies both the file digest and the trusted certificate chain.
        // Pass the path as data in an environment variable, never interpolate it into executable script.
        const string script = "$s = Get-AuthenticodeSignature -LiteralPath $env:TOOLSBOX_RUNTIME_PACKAGE; if ($s.Status -eq 'Valid' -and $s.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -eq 'Microsoft Corporation') { exit 0 }; exit 1";
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        start.Environment["TOOLSBOX_RUNTIME_PACKAGE"] = path;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动数字签名校验。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync();
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException("Microsoft 数字签名校验超时，请检查网络后重试。");
        }
        return process.ExitCode == 0;
    }

    public async Task<int> InstallAsync(string path)
    {
        if (WebResourceLauncher.IsAdministrator()) throw new InvalidOperationException("WebView2 安装必须从普通权限窗口启动。");
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/silent"); start.ArgumentList.Add("/install");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 WebView2 安装程序。");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
