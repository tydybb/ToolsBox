using System.Collections.ObjectModel;
using System.IO;
using ToolsBox.App.Infrastructure;
using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.App.NetworkTraffic;

public sealed class NetworkTrafficViewModel : ObservableObject, IDisposable
{
    private readonly INetworkTrafficSource _source;
    private readonly IBandwidthLimitService _limitService;
    private readonly NetworkTrafficAggregator _aggregator;
    private readonly INetworkTrafficClock _clock;
    private readonly INetworkTrafficDispatcher _dispatcher;
    private CancellationTokenSource? _sessionCancellation;
    private Task? _readerTask;
    private Task? _sampleTask;
    private IReadOnlyList<ApplicationTrafficSnapshot> _latestSnapshot = [];
    private IReadOnlyList<BandwidthLimitRule> _rules = [];
    private string _searchText = string.Empty;
    private bool _showActiveOnly;
    private bool _isMonitoring;
    private string _statusText = "点击“开始监控”并通过管理员授权";
    private string _lastSampleText = "尚未采样";
    private bool _hasDroppedEvents;
    private bool _disposed;

    public NetworkTrafficViewModel(
        INetworkTrafficSource source,
        IBandwidthLimitService limitService,
        IProcessMetadataProvider metadataProvider,
        INetworkTrafficClock clock,
        INetworkTrafficDispatcher dispatcher)
    {
        _source = source;
        _limitService = limitService;
        _aggregator = new NetworkTrafficAggregator(metadataProvider);
        _clock = clock;
        _dispatcher = dispatcher;
        StartCommand = new AsyncRelayCommand(StartAsync, () => !IsMonitoring);
        StopCommand = new AsyncRelayCommand(StopAsync, () => IsMonitoring);
    }

    public ObservableCollection<ApplicationTrafficItemViewModel> Items { get; } = [];
    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value)) ApplyFilter();
        }
    }

    public bool ShowActiveOnly
    {
        get => _showActiveOnly;
        set
        {
            if (SetProperty(ref _showActiveOnly, value)) ApplyFilter();
        }
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        private set
        {
            if (SetProperty(ref _isMonitoring, value))
            {
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string LastSampleText
    {
        get => _lastSampleText;
        private set => SetProperty(ref _lastSampleText, value);
    }

    public bool HasDroppedEvents
    {
        get => _hasDroppedEvents;
        private set => SetProperty(ref _hasDroppedEvents, value);
    }

    public async Task StartAsync()
    {
        if (IsMonitoring || _disposed)
        {
            return;
        }

        await CancelSessionLoopsAsync().ConfigureAwait(false);
        _sessionCancellation = new CancellationTokenSource();
        CancellationToken token = _sessionCancellation.Token;
        _aggregator.BeginSession(_clock.UtcNow);
        _latestSnapshot = [];
        await _dispatcher.InvokeAsync(() => Items.Clear()).ConfigureAwait(false);
        StatusText = "正在等待管理员授权…";
        try
        {
            await _source.StartAsync(token).ConfigureAwait(false);
            try
            {
                _rules = await _limitService.GetRulesAsync(token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _rules = [];
                StatusText = $"监控已启动，读取限速规则失败：{exception.Message}";
            }

            IsMonitoring = true;
            if (!StatusText.StartsWith("监控已启动，", StringComparison.Ordinal))
            {
                StatusText = "正在监控；当前仅支持上传限速";
            }

            _readerTask = ReadTrafficAsync(token);
            _sampleTask = SampleAsync(token);
            await Task.Yield();
        }
        catch (OperationCanceledException exception) when (!token.IsCancellationRequested)
        {
            StatusText = exception.Message;
            IsMonitoring = false;
        }
        catch (Exception exception)
        {
            StatusText = $"启动失败：{exception.Message}";
            IsMonitoring = false;
        }
    }

    public async Task StopAsync()
    {
        if (!IsMonitoring)
        {
            return;
        }

        IsMonitoring = false;
        _sessionCancellation?.Cancel();
        try
        {
            await _source.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusText = $"停止监控时发生错误：{exception.Message}";
        }

        await AwaitLoopTasksAsync().ConfigureAwait(false);
        _aggregator.MarkStopped(_clock.UtcNow);
        _latestSnapshot = await _aggregator.CreateSnapshotAsync(_clock.UtcNow).ConfigureAwait(false);
        await _dispatcher.InvokeAsync(() =>
        {
            ApplyFilter();
            StatusText = "监控已停止，已保留本次会话累计流量";
        }).ConfigureAwait(false);
    }

    public async Task SetUploadLimitAsync(
        ApplicationTrafficItemViewModel item,
        decimal value,
        BandwidthUnit unit,
        CancellationToken cancellationToken = default)
    {
        if (!item.CanLimit || !Path.IsPathFullyQualified(item.ExecutablePath))
        {
            throw new InvalidOperationException("该程序路径不可用于设置限速。");
        }

        ulong bitsPerSecond = BandwidthLimitRule.ToBitsPerSecond(value, unit);
        _ = await _limitService.SetUploadLimitAsync(item.ExecutablePath, bitsPerSecond, cancellationToken)
            .ConfigureAwait(false);
        await RefreshRulesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveUploadLimitAsync(
        ApplicationTrafficItemViewModel item,
        CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(item.ExecutablePath))
        {
            throw new InvalidOperationException("该程序路径不可用于解除限速。");
        }

        await _limitService.RemoveUploadLimitAsync(item.ExecutablePath, cancellationToken).ConfigureAwait(false);
        await RefreshRulesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshRulesAsync(CancellationToken cancellationToken)
    {
        _rules = await _limitService.GetRulesAsync(cancellationToken).ConfigureAwait(false);
        await _dispatcher.InvokeAsync(ApplyFilter).ConfigureAwait(false);
    }

    private async Task ReadTrafficAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (NetworkTrafficDelta delta in _source.ReadAllAsync(cancellationToken))
            {
                _aggregator.Apply([delta]);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await HandleBackgroundFailureAsync(exception).ConfigureAwait(false);
        }
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (DateTimeOffset sampledAt in _clock.GetTicksAsync(cancellationToken))
            {
                _latestSnapshot = await _aggregator.CreateSnapshotAsync(sampledAt, cancellationToken).ConfigureAwait(false);
                bool dropped = _source.DroppedEventCount > 0;
                await _dispatcher.InvokeAsync(() =>
                {
                    HasDroppedEvents = dropped;
                    LastSampleText = $"最近采样：{sampledAt.ToLocalTime():HH:mm:ss}";
                    ApplyFilter();
                    StatusText = dropped
                        ? "监控中；事件队列曾溢出，统计可能不完整"
                        : $"监控中，共 {Items.Count} 个软件；当前仅支持上传限速";
                }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await HandleBackgroundFailureAsync(exception).ConfigureAwait(false);
        }
    }

    private async Task HandleBackgroundFailureAsync(Exception exception)
    {
        _sessionCancellation?.Cancel();
        try
        {
            await _source.StopAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        await _dispatcher.InvokeAsync(() =>
        {
            IsMonitoring = false;
            StatusText = $"网络辅助进程已断开：{exception.Message}";
        }).ConfigureAwait(false);
    }

    private void ApplyFilter()
    {
        IReadOnlyList<ApplicationTrafficSnapshot> filtered = NetworkTrafficFilter.Apply(
            _latestSnapshot,
            SearchText,
            ShowActiveOnly);
        Dictionary<string, ApplicationTrafficItemViewModel> existing = Items.ToDictionary(
            item => item.Key,
            StringComparer.OrdinalIgnoreCase);
        Items.Clear();
        foreach (ApplicationTrafficSnapshot snapshot in filtered)
        {
            BandwidthLimitRule? rule = FindRule(snapshot.ExecutablePath);
            if (!existing.TryGetValue(snapshot.Key, out ApplicationTrafficItemViewModel? item))
            {
                item = new ApplicationTrafficItemViewModel(snapshot, rule);
            }
            else
            {
                item.Update(snapshot, rule);
            }

            Items.Add(item);
        }
    }

    private BandwidthLimitRule? FindRule(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        string fullPath = Path.GetFullPath(executablePath);
        return _rules.FirstOrDefault(rule =>
            string.Equals(Path.GetFullPath(rule.ExecutablePath), fullPath, StringComparison.OrdinalIgnoreCase));
    }

    private async Task CancelSessionLoopsAsync()
    {
        _sessionCancellation?.Cancel();
        await AwaitLoopTasksAsync().ConfigureAwait(false);
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
    }

    private async Task AwaitLoopTasksAsync()
    {
        Task[] tasks = new[] { _readerTask, _sampleTask }.OfType<Task>().ToArray();
        _readerTask = null;
        _sampleTask = null;
        if (tasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sessionCancellation?.Cancel();
        try
        {
            _source.StopAsync().GetAwaiter().GetResult();
            _source.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (!ReferenceEquals(_source, _limitService) && _limitService is IAsyncDisposable disposableLimitService)
            {
                disposableLimitService.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch
        {
        }
        finally
        {
            _sessionCancellation?.Dispose();
            _sessionCancellation = null;
        }
    }
}
