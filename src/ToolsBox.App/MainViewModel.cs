using ToolsBox.App.FileUnlocking;
using ToolsBox.App.Infrastructure;
using ToolsBox.App.NetworkTraffic;
using ToolsBox.App.Ports;

namespace ToolsBox.App;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private object _currentTool;

    public MainViewModel(
        PortMonitorViewModel portMonitor,
        FileUnlockerViewModel fileUnlocker,
        NetworkTrafficViewModel networkTraffic)
    {
        PortMonitor = portMonitor;
        FileUnlocker = fileUnlocker;
        NetworkTraffic = networkTraffic;
        _currentTool = PortMonitor;
        ShowPortMonitorCommand = new RelayCommand(() => CurrentTool = PortMonitor);
        ShowFileUnlockerCommand = new RelayCommand(() => CurrentTool = FileUnlocker);
        ShowNetworkTrafficCommand = new RelayCommand(() => CurrentTool = NetworkTraffic);
    }

    public PortMonitorViewModel PortMonitor { get; }
    public FileUnlockerViewModel FileUnlocker { get; }
    public NetworkTrafficViewModel NetworkTraffic { get; }
    public RelayCommand ShowPortMonitorCommand { get; }
    public RelayCommand ShowFileUnlockerCommand { get; }
    public RelayCommand ShowNetworkTrafficCommand { get; }

    public object CurrentTool
    {
        get => _currentTool;
        private set => SetProperty(ref _currentTool, value);
    }

    public Task InitializeAsync() => PortMonitor.InitializeAsync();

    public void Dispose()
    {
        PortMonitor.Dispose();
        FileUnlocker.Dispose();
        NetworkTraffic.Dispose();
    }
}
