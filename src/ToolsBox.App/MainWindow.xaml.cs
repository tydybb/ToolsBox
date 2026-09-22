using System.Windows;
using ToolsBox.App.FileUnlocking;
using ToolsBox.App.Ports;
using ToolsBox.App.NetworkTraffic;
using ToolsBox.Windows.FileUnlocking;
using ToolsBox.Windows.Ports;
using ToolsBox.Windows.NetworkTraffic;
using System.ComponentModel;
using System.Windows.Interop;
using ToolsBox.App.Infrastructure;
using ToolsBox.App.WorkCountdown;
using ToolsBox.App.Views;

namespace ToolsBox.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private ShellFileDropReceiver? _fileDrops;
    private readonly Action<WorkCountdownViewModel> _showOffWorkReminder;
    private readonly Action<WorkCountdownViewModel> _showWorkFinished;
    private OffWorkReminderWindow? _offWorkReminder;
    private System.Diagnostics.Process? _webResourceProcess;

    private void OpenWebResources(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_webResourceProcess is { HasExited: false })
            { MessageBox.Show(this, "网页资源下载窗口已打开，请在任务栏切换到该窗口。"); return; }
            _webResourceProcess?.Dispose();
            _webResourceProcess = WebResources.WebResourceLauncher.Launch();
        }
        catch (Exception error)
        {
            string detail = error is Win32Exception native ? native.Message : $"启动异常：{error.GetType().Name}。";
            MessageBox.Show(this, "无法以普通权限启动浏览窗口。\n\n" + detail + "\n\n这不是主程序缺少管理员权限。请保留上述错误信息用于排查。", "网页资源下载", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public MainWindow() : this(new WorkCountdownViewModel()) { }

    public MainWindow(WorkCountdownViewModel countdown, Action<WorkCountdownViewModel>? showReminder = null)
        : this(countdown, showReminder, null) { }

    public MainWindow(WorkCountdownViewModel countdown, Action<WorkCountdownViewModel>? showReminder,
        Action<WorkCountdownViewModel>? showFinished)
    {
        ArgumentNullException.ThrowIfNull(countdown);
        InitializeComponent();
        _showOffWorkReminder = showReminder ?? ShowOffWorkReminder;
        _showWorkFinished = showFinished ?? ShowWorkFinished;
        var networkClient = new ElevatedNetworkClient();
        _viewModel = new MainViewModel(
            new PortMonitorViewModel(new WindowsPortSnapshotProvider()),
            new FileUnlockerViewModel(new WindowsFileLockService()),
            new NetworkTrafficViewModel(
                networkClient,
                networkClient,
                new WindowsProcessMetadataProvider(),
                new SystemNetworkTrafficClock(),
                new WpfNetworkTrafficDispatcher(Dispatcher)), countdown);
        DataContext = _viewModel;
        SourceInitialized += OnSourceInitialized;
        _viewModel.PropertyChanged += OnDropStateChanged;
        _viewModel.FileUnlocker.PropertyChanged += OnDropStateChanged;
        _viewModel.WorkCountdown.OffWorkReached += OnOffWorkReached;
        _viewModel.WorkCountdown.WorkFinished += OnWorkFinished;
        _viewModel.WorkCountdown.PropertyChanged += OnCountdownChanged;
        Loaded += OnLoaded;
        Closing += (_, args) =>
        {
            try
            {
                if (_webResourceProcess is { HasExited: false } &&
                    MessageBox.Show(this, "网页资源窗口仍在运行。退出工具箱会关闭该窗口并取消未完成的下载，确定退出？", "退出宝哥工具箱", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                    args.Cancel = true;
            }
            catch (InvalidOperationException) { }
        };
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();

    private void OnOffWorkReached(object? sender, EventArgs e) => _showOffWorkReminder(_viewModel.WorkCountdown);

    private void OnWorkFinished(object? sender, EventArgs e) => _showWorkFinished(_viewModel.WorkCountdown);

    private void ShowWorkFinished(WorkCountdownViewModel countdown)
    {
        MessageBox.Show(this, $"{countdown.FinishMessage}\n\n{countdown.FinishDetails}", "下班提醒",
            MessageBoxButton.OK, countdown.IsEarlyDeparture ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void ShowOffWorkReminder(WorkCountdownViewModel countdown)
    {
        _offWorkReminder?.Close();
        var reminder = new OffWorkReminderWindow(countdown);
        reminder.Closed += (_, _) => { if (ReferenceEquals(_offWorkReminder, reminder)) _offWorkReminder = null; };
        _offWorkReminder = reminder;
        // Modeless and not owned by the main window: it can be seen while the toolbox is minimized.
        // ShowActivated=false prevents stealing focus from a process-termination confirmation.
        reminder.Show();
    }

    private void OnCountdownChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkCountdownViewModel.IsOverdue) && !_viewModel.WorkCountdown.IsOverdue)
            _offWorkReminder?.Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            _fileDrops = new ShellFileDropReceiver(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle), OnFilesDropped);
            UpdateDropState();
        }
        catch (Exception exception)
        {
            _viewModel.FileUnlocker.ReportDropUnavailable(exception.Message);
        }
    }

    private void OnDropStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CurrentTool) or nameof(FileUnlockerViewModel.IsBusy)) UpdateDropState();
    }

    private void UpdateDropState()
    {
        try
        {
            _fileDrops?.SetEnabled(_viewModel.IsFileUnlockerSelected && !_viewModel.FileUnlocker.IsBusy);
        }
        catch (Exception exception)
        {
            _fileDrops?.Dispose();
            _fileDrops = null;
            _viewModel.FileUnlocker.ReportDropUnavailable(exception.Message);
        }
    }

    private async void OnFilesDropped(string[] paths)
    {
        if (_viewModel.IsFileUnlockerSelected) await _viewModel.FileUnlocker.HandleDroppedPathsAsync(paths);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnDropStateChanged;
        _viewModel.FileUnlocker.PropertyChanged -= OnDropStateChanged;
        _viewModel.WorkCountdown.OffWorkReached -= OnOffWorkReached;
        _viewModel.WorkCountdown.WorkFinished -= OnWorkFinished;
        _viewModel.WorkCountdown.PropertyChanged -= OnCountdownChanged;
        _offWorkReminder?.Close();
        _fileDrops?.Dispose();
        _viewModel.Dispose();
        _webResourceProcess?.Dispose();
    }
}
