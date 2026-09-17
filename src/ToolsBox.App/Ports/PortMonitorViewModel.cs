using System.Collections.ObjectModel;
using ToolsBox.App.Infrastructure;
using ToolsBox.Core.Ports;

namespace ToolsBox.App.Ports;

public sealed class PortMonitorViewModel : ObservableObject, IDisposable
{
    private readonly PortMonitorService _monitorService;
    private CancellationTokenSource? _loopCancellation;
    private string _searchText = string.Empty;
    private int _refreshIntervalSeconds = 2;
    private bool _isPaused;
    private bool _isRefreshing;
    private string _statusText = "准备就绪";
    private string _lastRefreshText = "尚未刷新";
    private string? _errorText;

    public PortMonitorViewModel(IPortSnapshotProvider provider)
    {
        _monitorService = new PortMonitorService(provider);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsRefreshing);
        TogglePauseCommand = new RelayCommand(() => IsPaused = !IsPaused);
    }

    public ObservableCollection<PortEntry> Entries { get; } = [];
    public IReadOnlyList<int> RefreshIntervals { get; } = [1, 2, 5, 10];
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand TogglePauseCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public int RefreshIntervalSeconds
    {
        get => _refreshIntervalSeconds;
        set
        {
            if (SetProperty(ref _refreshIntervalSeconds, value))
            {
                RestartLoop();
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(PauseButtonText));
                StatusText = value ? "自动刷新已暂停" : $"每 {RefreshIntervalSeconds} 秒自动刷新";
            }
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PauseButtonText => IsPaused ? "继续" : "暂停";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string LastRefreshText
    {
        get => _lastRefreshText;
        private set => SetProperty(ref _lastRefreshText, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public async Task InitializeAsync()
    {
        await RefreshAsync();
        RestartLoop();
    }

    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        StatusText = "正在扫描端口…";
        try
        {
            bool refreshed = await _monitorService.RefreshAsync();
            ErrorText = _monitorService.LastError;
            if (refreshed)
            {
                ApplyFilter();
                LastRefreshText = $"最近刷新：{_monitorService.LastRefreshedAt:HH:mm:ss}";
                StatusText = $"已显示 {Entries.Count} 条，共 {_monitorService.Snapshot.Count} 条";
            }
            else if (HasError)
            {
                StatusText = "刷新失败，已保留上次结果";
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void ApplyFilter()
    {
        IReadOnlyList<PortEntry> filtered = PortEntryFilter.Apply(_monitorService.Snapshot, SearchText);
        Entries.Clear();
        foreach (PortEntry entry in filtered)
        {
            Entries.Add(entry);
        }

        StatusText = $"已显示 {Entries.Count} 条，共 {_monitorService.Snapshot.Count} 条";
    }

    private void RestartLoop()
    {
        _loopCancellation?.Cancel();
        _loopCancellation?.Dispose();
        _loopCancellation = new CancellationTokenSource();
        _ = RunRefreshLoopAsync(_loopCancellation.Token);
        if (!IsPaused)
        {
            StatusText = $"每 {RefreshIntervalSeconds} 秒自动刷新";
        }
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(RefreshIntervalSeconds));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!IsPaused)
                {
                    await RefreshAsync();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        _loopCancellation?.Cancel();
        _loopCancellation?.Dispose();
        _loopCancellation = null;
    }
}
