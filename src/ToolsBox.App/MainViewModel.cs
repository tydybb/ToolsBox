using ToolsBox.App.FileUnlocking;
using ToolsBox.App.Infrastructure;
using ToolsBox.App.Ports;

namespace ToolsBox.App;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private object _currentTool;

    public MainViewModel(PortMonitorViewModel portMonitor, FileUnlockerViewModel fileUnlocker)
    {
        PortMonitor = portMonitor;
        FileUnlocker = fileUnlocker;
        _currentTool = PortMonitor;
        ShowPortMonitorCommand = new RelayCommand(() => CurrentTool = PortMonitor);
        ShowFileUnlockerCommand = new RelayCommand(() => CurrentTool = FileUnlocker);
    }

    public PortMonitorViewModel PortMonitor { get; }
    public FileUnlockerViewModel FileUnlocker { get; }
    public RelayCommand ShowPortMonitorCommand { get; }
    public RelayCommand ShowFileUnlockerCommand { get; }

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
    }
}
