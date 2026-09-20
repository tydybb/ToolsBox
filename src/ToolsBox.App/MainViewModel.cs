using ToolsBox.App.FileUnlocking;
using ToolsBox.App.Infrastructure;
using ToolsBox.App.NetworkTraffic;
using ToolsBox.App.Ports;
using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private object _currentTool;

    public MainViewModel(
        PortMonitorViewModel portMonitor,
        FileUnlockerViewModel fileUnlocker,
        NetworkTrafficViewModel networkTraffic,
        WorkCountdownViewModel? workCountdown = null)
    {
        PortMonitor = portMonitor;
        FileUnlocker = fileUnlocker;
        NetworkTraffic = networkTraffic;
        WorkCountdown = workCountdown ?? new WorkCountdownViewModel();
        _currentTool = PortMonitor;
        ShowPortMonitorCommand = new RelayCommand(() => CurrentTool = PortMonitor);
        ShowFileUnlockerCommand = new RelayCommand(() => CurrentTool = FileUnlocker);
        ShowNetworkTrafficCommand = new RelayCommand(() => CurrentTool = NetworkTraffic);
        ShowWorkCountdownCommand = new RelayCommand(() => CurrentTool = WorkCountdown);
    }

    public PortMonitorViewModel PortMonitor { get; }
    public FileUnlockerViewModel FileUnlocker { get; }
    public NetworkTrafficViewModel NetworkTraffic { get; }
    public WorkCountdownViewModel WorkCountdown { get; }
    public RelayCommand ShowWorkCountdownCommand { get; }
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
                OnPropertyChanged(nameof(IsWorkCountdownSelected));
            }
        }
    }

    public bool IsPortMonitorSelected => ReferenceEquals(CurrentTool, PortMonitor);
    public bool IsFileUnlockerSelected => ReferenceEquals(CurrentTool, FileUnlocker);
    public bool IsNetworkTrafficSelected => ReferenceEquals(CurrentTool, NetworkTraffic);
    public bool IsWorkCountdownSelected => ReferenceEquals(CurrentTool, WorkCountdown);

    public Task InitializeAsync() => PortMonitor.InitializeAsync();

    public void Dispose()
    {
        PortMonitor.Dispose();
        FileUnlocker.Dispose();
        NetworkTraffic.Dispose();
        WorkCountdown.Dispose();
    }
}
