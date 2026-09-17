using System.Windows;
using ToolsBox.App.FileUnlocking;
using ToolsBox.App.Ports;
using ToolsBox.App.NetworkTraffic;
using ToolsBox.Windows.FileUnlocking;
using ToolsBox.Windows.Ports;
using ToolsBox.Windows.NetworkTraffic;

namespace ToolsBox.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

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
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();
    private void OnClosed(object? sender, EventArgs e) => _viewModel.Dispose();
}
