using ToolsBox.App.FileUnlocking;
using ToolsBox.App.Infrastructure;
using ToolsBox.App.NetworkTraffic;
using ToolsBox.App.Ports;
using ToolsBox.App.ArchiveRecovery;

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
        ArchiveRecovery = new ArchiveRecoveryViewModel();
        _currentTool = PortMonitor;
        ShowPortMonitorCommand = new RelayCommand(() => CurrentTool = PortMonitor);
        ShowFileUnlockerCommand = new RelayCommand(() => CurrentTool = FileUnlocker);
        ShowNetworkTrafficCommand = new RelayCommand(() => CurrentTool = NetworkTraffic);
        ShowArchiveRecoveryCommand = new RelayCommand(() => CurrentTool = ArchiveRecovery);
    }

    public PortMonitorViewModel PortMonitor { get; }
    public FileUnlockerViewModel FileUnlocker { get; }
    public NetworkTrafficViewModel NetworkTraffic { get; }
    public ArchiveRecoveryViewModel ArchiveRecovery { get; }
    public RelayCommand ShowArchiveRecoveryCommand { get; }
    public RelayCommand ShowPortMonitorCommand { get; }
    public RelayCommand ShowFileUnlockerCommand { get; }
    public RelayCommand ShowNetworkTrafficCommand { get; }

    public object CurrentTool
    {
        get => _currentTool;
        private set
        {
            if (SetProperty(ref _currentTool, value))
            {
                OnPropertyChanged(nameof(IsPortMonitorSelected));
                OnPropertyChanged(nameof(IsFileUnlockerSelected));
                OnPropertyChanged(nameof(IsNetworkTrafficSelected));
                OnPropertyChanged(nameof(IsArchiveRecoverySelected));
            }
        }
    }

    public bool IsPortMonitorSelected => ReferenceEquals(CurrentTool, PortMonitor);
    public bool IsFileUnlockerSelected => ReferenceEquals(CurrentTool, FileUnlocker);
    public bool IsNetworkTrafficSelected => ReferenceEquals(CurrentTool, NetworkTraffic);
    public bool IsArchiveRecoverySelected => ReferenceEquals(CurrentTool, ArchiveRecovery);

    public Task InitializeAsync() => PortMonitor.InitializeAsync();

    public void Dispose()
    {
        PortMonitor.Dispose();
        FileUnlocker.Dispose();
        NetworkTraffic.Dispose();
        ArchiveRecovery.Dispose();
    }
}
