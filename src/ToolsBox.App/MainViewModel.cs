using ToolsBox.App.FileUnlocking;
using ToolsBox.App.Infrastructure;
using ToolsBox.App.NetworkTraffic;
using ToolsBox.App.Ports;
using ToolsBox.App.WorkCountdown;

namespace ToolsBox.App;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private object _currentTool;
    private readonly Lazy<Lottery.LotteryViewModel> _lottery = new(() => new());

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
        ShowWebResourcesCommand = new RelayCommand(() => CurrentTool = WebResources);
        ShowLotteryCommand = new RelayCommand(() => CurrentTool = Lottery);
    }

    public PortMonitorViewModel PortMonitor { get; }
    public FileUnlockerViewModel FileUnlocker { get; }
    public NetworkTrafficViewModel NetworkTraffic { get; }
    public WorkCountdownViewModel WorkCountdown { get; }
    public WebResources.WebResourcesHome WebResources { get; } = new();
    public RelayCommand ShowWebResourcesCommand { get; }
    public Lottery.LotteryViewModel Lottery => _lottery.Value;
    public RelayCommand ShowLotteryCommand { get; }
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
                OnPropertyChanged(nameof(IsWebResourcesSelected));
                OnPropertyChanged(nameof(IsLotterySelected));
            }
        }
    }

    public bool IsPortMonitorSelected => ReferenceEquals(CurrentTool, PortMonitor);
    public bool IsFileUnlockerSelected => ReferenceEquals(CurrentTool, FileUnlocker);
    public bool IsNetworkTrafficSelected => ReferenceEquals(CurrentTool, NetworkTraffic);
    public bool IsWorkCountdownSelected => ReferenceEquals(CurrentTool, WorkCountdown);
    public bool IsWebResourcesSelected => ReferenceEquals(CurrentTool, WebResources);
    public bool IsLotterySelected => _lottery.IsValueCreated && ReferenceEquals(CurrentTool, _lottery.Value);

    public Task InitializeAsync() => PortMonitor.InitializeAsync();

    public void Dispose()
    {
        PortMonitor.Dispose();
        FileUnlocker.Dispose();
        NetworkTraffic.Dispose();
        WorkCountdown.Dispose();
        if (_lottery.IsValueCreated) _lottery.Value.Dispose();
    }
}
