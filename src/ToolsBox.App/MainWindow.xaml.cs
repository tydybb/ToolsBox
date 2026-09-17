using System.Windows;
using ToolsBox.App.FileUnlocking;
using ToolsBox.App.Ports;
using ToolsBox.Windows.FileUnlocking;
using ToolsBox.Windows.Ports;

namespace ToolsBox.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(
            new PortMonitorViewModel(new WindowsPortSnapshotProvider()),
            new FileUnlockerViewModel(new WindowsFileLockService()));
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();
    private void OnClosed(object? sender, EventArgs e) => _viewModel.Dispose();
}
