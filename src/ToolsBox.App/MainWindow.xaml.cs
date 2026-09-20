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
    }
}
