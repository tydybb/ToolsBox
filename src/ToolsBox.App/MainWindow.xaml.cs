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

namespace ToolsBox.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private ShellFileDropReceiver? _fileDrops;

    public MainWindow()
    {
        InitializeComponent();
        var networkClient = new ElevatedNetworkClient();
        _viewModel = new MainViewModel(
            new PortMonitorViewModel(new WindowsPortSnapshotProvider()),
            new FileUnlockerViewModel(new WindowsFileLockService()),
            new NetworkTrafficViewModel(
                networkClient,
                networkClient,
                new WindowsProcessMetadataProvider(),
                new SystemNetworkTrafficClock(),
                new WpfNetworkTrafficDispatcher(Dispatcher)));
        DataContext = _viewModel;
        SourceInitialized += OnSourceInitialized;
        _viewModel.PropertyChanged += OnDropStateChanged;
        _viewModel.FileUnlocker.PropertyChanged += OnDropStateChanged;
        _viewModel.ArchiveRecovery.PropertyChanged += OnDropStateChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();

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
            _viewModel.ArchiveRecovery.ReportDropUnavailable();
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
            _fileDrops?.SetEnabled((_viewModel.IsFileUnlockerSelected && !_viewModel.FileUnlocker.IsBusy)
                || (_viewModel.IsArchiveRecoverySelected && !_viewModel.ArchiveRecovery.IsBusy));
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
        else if (_viewModel.IsArchiveRecoverySelected) _viewModel.ArchiveRecovery.HandleDroppedPaths(paths);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnDropStateChanged;
        _viewModel.FileUnlocker.PropertyChanged -= OnDropStateChanged;
        _viewModel.ArchiveRecovery.PropertyChanged -= OnDropStateChanged;
        _fileDrops?.Dispose();
        _viewModel.Dispose();
    }
}
