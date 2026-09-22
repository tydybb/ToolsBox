using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using ToolsBox.MediaDownloads;

namespace ToolsBox.App.WebResources;

public partial class EnvironmentWindow : Window
{
    private readonly ComponentManager components;
    private readonly WebRuntimeInstaller runtime = new();
    private CancellationTokenSource? operation;
    private bool busy, closeWhenFinished, runtimeMissing, videoMissing, detectionSucceeded;
    public bool RuntimeInstalled { get; private set; }
    public bool IsInstallerRunning => runtime.IsInstalling;

    public EnvironmentWindow(ComponentManager components)
    {
        this.components = components;
        InitializeComponent();
        Loaded += async (_, _) => await RunAsync(async ct => { await DetectAsync(ct); OperationStatus.Text = "检测完成。"; });
        Closing += HandleClosing;
    }

    private async Task DetectAsync(CancellationToken ct)
    {
        detectionSucceeded = false;
        Report(new("正在检查本机环境"));
        var version = runtime.DetectVersion();
        runtimeMissing = version == null;
        RuntimeStatus.Text = runtimeMissing ? "缺失 · 需要安装以打开网页" : "已安装 · " + version;
        videoMissing = !await components.IsReadyAsync(ct);
        VideoStatus.Text = videoMissing ? "缺失、不完整或检查失败 · 需要安装完整组件包以下载视频" : "已安装 · 三个程序版本检查通过";
        var videoDetails = videoMissing
            ? $"视频组件不可用。安装版本：yt-dlp {ComponentManager.YtDlpVersion} / FFmpeg {ComponentManager.FFmpegVersion}\n{ComponentManager.YtDlpSource}\n{ComponentManager.FFmpegSource}"
            : await components.DescribeAsync(ct);
        Details.Text = "WebView2 Evergreen x64（Microsoft 官方，安装时获取当前版本）\n" + WebRuntimeInstaller.Source +
            "\n安装包必须通过 Windows Authenticode 校验且发布者为 Microsoft Corporation。\n以普通用户权限启动；Microsoft Edge Updater 可能将其作为系统组件管理。\n\n" + videoDetails;
        ct.ThrowIfCancellationRequested();
        detectionSucceeded = true;
        OperationProgress.IsIndeterminate = false;
        OperationProgress.Value = 0;
    }

    private async void DetectAgain(object sender, RoutedEventArgs e) => await RunAsync(async ct => { await DetectAsync(ct); OperationStatus.Text = "检测完成。"; });

    private async void InstallMissing(object sender, RoutedEventArgs e)
    {
        if (busy || !detectionSucceeded) return;
        var items = new List<string>();
        if (runtimeMissing) items.Add("• WebView2：Microsoft 官方 Evergreen x64 安装包，通常约 150–250 MB；下载后校验 Microsoft 数字签名，再以普通用户权限安装。");
        if (videoMissing) items.Add($"• 视频组件包：yt-dlp {ComponentManager.YtDlpVersion}（GitHub 官方发布）及 FFmpeg {ComponentManager.FFmpegVersion}（gyan.dev 构建），通常共约 100–200 MB，校验 SHA256。");
        if (items.Count == 0) return;
        if (MessageBox.Show(this, string.Join("\n\n", items) + "\n\n大小仅为估计，以下载进度为准。只安装缺失项；不完整的视频组件按整包安装。是否继续？", "确认下载并安装", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunAsync(async ct =>
        {
            var progress = new Progress<DownloadProgress>(Report);
            // Recheck immediately before download; another instance may have installed an item.
            if (runtime.DetectVersion() == null)
            {
                await runtime.InstallAsync(progress, ct);
                RuntimeInstalled = true;
                RuntimeStatus.Text = "已安装 · " + runtime.DetectVersion();
            }
            ct.ThrowIfCancellationRequested();
            if (!await components.IsReadyAsync(ct)) await components.InstallAsync(progress, ct);
            await DetectAsync(ct);
            OperationStatus.Text = "缺失项已安装，环境已就绪。";
            OperationProgress.Value = 100;
        });
    }

    private async void ImportComponents(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var picker = new OpenFolderDialog { Title = "选择包含 yt-dlp.exe、ffmpeg.exe、ffprobe.exe 的可信目录" };
        if (picker.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "导入后会执行这三个程序以检查版本。请确认目录来自可信来源。继续？", "导入可信组件", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunAsync(async ct =>
        {
            Report(new("导入并检查视频组件"));
            await components.ImportAsync(picker.FolderName, ct);
            await DetectAsync(ct);
            OperationStatus.Text = "视频组件已导入。";
        });
    }

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        if (busy) return;
        busy = true;
        operation = new CancellationTokenSource();
        SetButtons();
        try { await work(operation.Token); }
        catch (OperationCanceledException) { OperationStatus.Text = "操作已取消，可重新检测或重试。"; }
        catch (Exception ex) { OperationStatus.Text = "操作失败，可重新检测后重试：" + ex.Message; }
        finally
        {
            operation.Dispose(); operation = null;
            busy = false; OperationProgress.IsIndeterminate = false;
            SetButtons();
            if (closeWhenFinished) Close();
        }
    }

    private void SetButtons()
    {
        InstallMissingButton.IsEnabled = !busy && detectionSucceeded && (runtimeMissing || videoMissing);
        DetectButton.IsEnabled = ImportButton.IsEnabled = !busy;
        CloseButton.Content = busy ? "取消 / 关闭" : "关闭";
    }

    private void Report(DownloadProgress progress)
    {
        OperationStatus.Text = progress.Status + (progress.Percent.HasValue ? $" · {progress.Percent:F1}%" : "") + (progress.Detail == null ? "" : " · " + progress.Detail);
        OperationProgress.IsIndeterminate = !progress.Percent.HasValue;
        OperationProgress.Value = progress.Percent ?? 0;
    }

    public void RequestCloseForParentExit()
    {
        closeWhenFinished = true;
        if (busy) operation?.Cancel(); // The launched installer deliberately ignores cancellation.
        else Close();
    }

    private void HandleClosing(object? sender, CancelEventArgs e)
    {
        if (!busy) return;
        e.Cancel = true;
        if (runtime.IsInstalling)
        {
            OperationStatus.Text = "安装程序正在运行，请等待结束后再关闭。";
            return;
        }
        if (closeWhenFinished) return;
        if (MessageBox.Show(this, "取消当前检测或下载并关闭？", "关闭环境检测", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        { closeWhenFinished = true; operation?.Cancel(); }
    }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
