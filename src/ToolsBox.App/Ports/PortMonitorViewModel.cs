using System.Collections.ObjectModel;
using ToolsBox.App.Infrastructure;
using ToolsBox.Core.Ports;

namespace ToolsBox.App.Ports;

public sealed record PortSearchChoice(PortSearchField Field, string Label);

public sealed class PortMonitorViewModel : ObservableObject, IDisposable
{
    private readonly PortMonitorService _monitorService;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _loopCancellation;
    private string _searchText = string.Empty;
    private PortSearchField _searchField;
    private int _refreshIntervalSeconds = 2;
    private bool _isPaused = true;
    private bool _disposed;
    private bool _isRefreshing;
    private string _statusText = "尚未刷新，请点击“立即刷新”或开始自动刷新。";
    private string _lastRefreshText = "尚未刷新";
    private string? _errorText;

    public PortMonitorViewModel(IPortSnapshotProvider provider)
    {
        _monitorService = new PortMonitorService(provider);
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(_lifetimeCancellation.Token), () => !_disposed && !IsRefreshing);
        TogglePauseCommand = new RelayCommand(() => IsPaused = !IsPaused, () => !_disposed);
    }

    public ObservableCollection<PortEntry> Entries { get; } = [];
    public IReadOnlyList<int> RefreshIntervals { get; } = [1, 2, 5, 10];
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand TogglePauseCommand { get; }
    public IReadOnlyList<PortSearchChoice> SearchFields { get; } =
    [
        new(PortSearchField.All, "全部"),
        new(PortSearchField.Port, "端口"),
        new(PortSearchField.IpAddress, "IP 地址"),
        new(PortSearchField.ProcessId, "进程 PID"),
        new(PortSearchField.ProcessName, "进程名称")
    ];

    public PortSearchField SearchField
    {
        get => _searchField;
        set
        {
            if (_disposed) return;
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (SetProperty(ref _searchField, value))
            {
                OnPropertyChanged(nameof(SearchHint));
                ApplyFilter();
            }
        }
    }

    public string SearchHint => SearchField switch
    {
        PortSearchField.Port => "输入本地或远程端口号（精确匹配）",
        PortSearchField.IpAddress => "输入本地或远程 IP 地址片段",
        PortSearchField.ProcessId => "输入进程 PID（精确匹配）",
        PortSearchField.ProcessName => "输入进程名称片段",
        _ => "搜索端口、IP、PID 或名称"
    };

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!_disposed && SetProperty(ref _searchText, value))
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
            if (_disposed) return;
            if (!RefreshIntervals.Contains(value)) throw new ArgumentOutOfRangeException(nameof(value));
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
            if (!_disposed && SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(PauseButtonText));
                OnPropertyChanged(nameof(PauseGlyph));
                RestartLoop();
                if (!IsRefreshing) UpdateStatus();
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

    public string PauseButtonText => IsPaused ? "开始自动刷新" : "暂停自动刷新";
    public string PauseGlyph => IsPaused ? "\uE768" : "\uE769";

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

    // App startup prepares the view only. Scanning always requires an explicit user action.
    public Task InitializeAsync() => Task.CompletedTask;

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_disposed || IsRefreshing || cancellationToken.IsCancellationRequested) return;
        IsRefreshing = true;
        StatusText = "正在扫描端口…";
        try
        {
            bool refreshed = await _monitorService.RefreshAsync(cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested) return;
            ErrorText = _monitorService.LastError;
            if (refreshed)
            {
                ApplyFilter();
                LastRefreshText = $"最近刷新：{_monitorService.LastRefreshedAt:HH:mm:ss}";
                UpdateStatus();
            }
            else if (HasError)
            {
                StatusText = "刷新失败，已保留上次结果";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!_disposed) UpdateStatus();
        }
        finally
        {
            if (_disposed) _isRefreshing = false;
            else IsRefreshing = false;
        }
    }

    private void ApplyFilter()
    {
        IReadOnlyList<PortEntry> filtered = PortEntryFilter.Apply(_monitorService.Snapshot, SearchText, SearchField);
        Entries.Clear();
        foreach (PortEntry entry in filtered)
        {
            Entries.Add(entry);
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_disposed) return;
        string mode = IsPaused ? "手动刷新模式" : $"每 {RefreshIntervalSeconds} 秒自动刷新";
        StatusText = _monitorService.LastRefreshedAt is null
            ? $"尚未刷新 · {mode}"
            : $"已显示 {Entries.Count} 条，共 {_monitorService.Snapshot.Count} 条 · {mode}";
    }

    private void StopLoop()
    {
        var previous = _loopCancellation;
        _loopCancellation = null;
        previous?.Cancel();
    }

    private void RestartLoop()
    {
        StopLoop();
        if (_disposed || IsPaused) return;
        _loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _ = RunRefreshLoopAsync(_loopCancellation);
    }

    private async Task RunRefreshLoopAsync(CancellationTokenSource owner)
    {
        CancellationToken cancellationToken = owner.Token;
        try
        {
            await RefreshAsync(cancellationToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(RefreshIntervalSeconds));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!IsPaused)
                {
                    await RefreshAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_loopCancellation, owner)) _loopCancellation = null;
            owner.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopLoop();
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        RefreshCommand.RaiseCanExecuteChanged();
        TogglePauseCommand.RaiseCanExecuteChanged();
    }
}
