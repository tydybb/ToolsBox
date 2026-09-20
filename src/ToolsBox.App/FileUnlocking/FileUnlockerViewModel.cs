using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ToolsBox.App.Infrastructure;
using ToolsBox.Core.FileUnlocking;

namespace ToolsBox.App.FileUnlocking;

public sealed class FileUnlockerViewModel : ObservableObject, IDisposable
{
    private readonly IFileLockService _service;
    private CancellationTokenSource? _operationCancellation;
    private string _pathText = string.Empty;
    private string _statusText = "输入或拖入文件/文件夹开始检测";
    private bool _isBusy;
    private bool _isAdvancedExpanded;
    private bool _isDisposed;
    private bool _isUpdatingSelection;
    private bool? _allSelected = false;
    private int _selectedCount;
    private int _selectedProcessCount;
    private readonly HashSet<FileLockEntry> _observedEntries = new(ReferenceEqualityComparer.Instance);

    public FileUnlockerViewModel(IFileLockService service)
    {
        _service = service;
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(PathText));
        ElevatedScanCommand = new AsyncRelayCommand(ScanElevatedAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(PathText));
        ToggleSelectAllCommand = new RelayCommand(ToggleSelectAll, () => CanSelectEntries);
        Entries.CollectionChanged += OnEntriesChanged;
    }

    public ObservableCollection<FileLockEntry> Entries { get; } = [];
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand ElevatedScanCommand { get; }
    public RelayCommand ToggleSelectAllCommand { get; }
    public bool? AllSelected => _allSelected;
    public int SelectedCount => _selectedCount;
    public int SelectedProcessCount => _selectedProcessCount;
    public bool CanSelectEntries => !_isDisposed && !IsBusy && Entries.Count > 0;
    public bool CanActOnSelectedEntries => CanSelectEntries && SelectedCount > 0;
    public bool CanEditPath => !_isDisposed && !IsBusy;

    public string PathText
    {
        get => _pathText;
        set
        {
            if (!CanEditPath) return;
            if (SetProperty(ref _pathText, value))
            {
                Entries.Clear();
                StatusText = "路径已变化，请重新检测占用。";
                ScanCommand.RaiseCanExecuteChanged();
                ElevatedScanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ScanCommand.RaiseCanExecuteChanged();
                ElevatedScanCommand.RaiseCanExecuteChanged();
                RefreshSelectionAvailability();
                OnPropertyChanged(nameof(CanEditPath));
            }
        }
    }

    public bool IsAdvancedExpanded
    {
        get => _isAdvancedExpanded;
        set => SetProperty(ref _isAdvancedExpanded, value);
    }

    public async Task SetPathAndScanAsync(string path)
    {
        if (!CanEditPath) return;
        PathText = path;
        await ScanAsync();
    }

    public async Task HandleDroppedPathsAsync(IReadOnlyList<string> paths)
    {
        if (IsBusy) return;
        if (paths.Count != 1 || string.IsNullOrWhiteSpace(paths[0]))
        {
            StatusText = "请一次拖入一个文件或文件夹。";
            return;
        }
        await SetPathAndScanAsync(paths[0]);
    }

    public void ReportDropUnavailable(string reason) =>
        StatusText = $"拖放暂不可用，请使用选择文件/文件夹按钮：{reason}";

    public Task ScanAsync() => ScanCoreAsync(false);

    public Task ScanElevatedAsync() => ScanCoreAsync(true);

    private async Task ScanCoreAsync(bool elevated)
    {
        if (IsBusy || _isDisposed) return;
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        StatusText = elevated ? "正在等待管理员授权并扫描…" : "正在扫描系统文件句柄…";
        try
        {
            FileLockTarget target = FileLockTarget.FromExistingPath(PathText);
            IReadOnlyList<FileLockEntry> locks = elevated
                ? await _service.FindLocksElevatedAsync(target, _operationCancellation.Token)
                : await _service.FindLocksAsync(target, _operationCancellation.Token);
            if (_isDisposed) return;
            _isUpdatingSelection = true;
            try
            {
                Entries.Clear();
                foreach (FileLockEntry entry in locks)
                {
                    entry.IsSelected = false;
                    Entries.Add(entry);
                }
            }
            finally
            {
                _isUpdatingSelection = false;
                RefreshSelection();
            }

            StatusText = locks.Count == 0
                ? elevated
                    ? "管理员扫描未发现占用，可正常操作该路径"
                    : "当前权限未发现占用；若文件仍无法操作，请使用管理员扫描"
                : $"发现 {locks.Count} 个占用句柄";
            if (locks is FileLockScanResult { SkippedHandleCount: > 0 } partial)
                StatusText = $"发现 {locks.Count} 个占用句柄；跳过 {partial.SkippedHandleCount} 个查询超时的句柄，结果可能不完整。";
        }
        catch (OperationCanceledException)
        {
            StatusText = "扫描已取消";
        }
        catch (Exception exception)
        {
            Entries.Clear();
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<string> TerminateSelectedProcessesAsync(Func<IReadOnlyList<FileLockEntry>, bool> confirm)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        if (IsBusy || _isDisposed) return "请等待当前操作完成。";
        FileLockEntry[] selected = Entries.Where(item => item.IsSelected)
            .Select(item => item with { }).ToArray();
        if (selected.Length == 0)
        {
            return "请先勾选至少一条占用记录。";
        }

        var blocked = new List<string>();
        foreach (FileLockEntry entry in selected)
            if (!FileUnlockSafetyPolicy.CanOperate(entry, Environment.ProcessId, out string reason))
                blocked.Add($"{entry.ProcessName} (PID {entry.ProcessId})：{reason}");
        if (blocked.Count > 0)
            return StatusText = "已阻止本次操作，请先取消勾选以下受保护或无法验证的进程："
                + Environment.NewLine + string.Join(Environment.NewLine, blocked);
        if (selected.GroupBy(item => item.ProcessId).Any(group => group.Select(item => item.ProcessStartedAt).Distinct().Count() > 1))
            return StatusText = "同一 PID 出现不同的进程身份，为避免误操作请重新扫描。";
        selected = selected.DistinctBy(item => item.ProcessId).ToArray();

        IsBusy = true;
        try
        {
            if (!confirm(Array.AsReadOnly(selected)) || _isDisposed)
                return StatusText = "已取消结束进程，未执行任何操作。";
            return await ExecuteActionsAsync(selected, _service.TerminateProcessAsync);
        }
        finally { IsBusy = false; }
    }

    public async Task<string> CloseSelectedHandlesAsync()
    {
        if (IsBusy || _isDisposed) return "请等待当前操作完成。";
        FileLockEntry[] selected = Entries.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return "请先勾选至少一条占用记录。";
        }

        return await ExecuteActionsAsync(selected, _service.CloseHandleAsync);
    }

    private async Task<string> ExecuteActionsAsync(
        IReadOnlyList<FileLockEntry> entries,
        Func<FileLockEntry, CancellationToken, Task<FileUnlockResult>> action)
    {
        IsBusy = true;
        int succeeded = 0;
        var failures = new List<string>();
        try
        {
            foreach (FileLockEntry entry in entries)
            {
                if (_isDisposed)
                {
                    failures.Add("工具页面已关闭，已停止后续操作。");
                    break;
                }
                FileUnlockResult result = await action(entry, CancellationToken.None);
                if (result.Succeeded)
                {
                    succeeded++;
                }
                else
                {
                    failures.Add($"{entry.ProcessName} ({entry.ProcessId})：{result.Message}");
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        if (!_isDisposed) await ScanAsync();
        string summary = $"成功 {succeeded} 项，失败 {failures.Count} 项。";
        if (failures.Count > 0)
        {
            summary += Environment.NewLine + string.Join(Environment.NewLine, failures);
        }

        StatusText = summary;
        return summary;
    }

    private void ToggleSelectAll()
    {
        if (!CanSelectEntries) return;
        bool select = AllSelected != true;
        _isUpdatingSelection = true;
        try
        {
            foreach (FileLockEntry entry in Entries) entry.IsSelected = select;
        }
        finally
        {
            _isUpdatingSelection = false;
            RefreshSelection();
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (FileLockEntry entry in _observedEntries) entry.PropertyChanged -= OnEntryPropertyChanged;
            _observedEntries.Clear();
            foreach (FileLockEntry entry in Entries) ObserveEntry(entry);
        }
        else
        {
            if (args.OldItems is not null)
                foreach (FileLockEntry entry in args.OldItems)
                    if (!Entries.Any(current => ReferenceEquals(current, entry)) && _observedEntries.Remove(entry))
                        entry.PropertyChanged -= OnEntryPropertyChanged;
            if (args.NewItems is not null)
                foreach (FileLockEntry entry in args.NewItems) ObserveEntry(entry);
        }
        RefreshSelection();
    }

    private void ObserveEntry(FileLockEntry entry)
    {
        if (_observedEntries.Add(entry)) entry.PropertyChanged += OnEntryPropertyChanged;
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(FileLockEntry.IsSelected))
            RefreshSelection();
    }

    private void RefreshSelection()
    {
        if (_isUpdatingSelection || _isDisposed) return;
        int count = 0;
        var processIds = new HashSet<int>();
        foreach (FileLockEntry entry in Entries)
        {
            if (!entry.IsSelected) continue;
            count++;
            processIds.Add(entry.ProcessId);
        }
        SetProperty(ref _selectedCount, count, nameof(SelectedCount));
        SetProperty(ref _selectedProcessCount, processIds.Count, nameof(SelectedProcessCount));
        SetProperty(ref _allSelected, count == 0 ? false : count == Entries.Count ? true : null, nameof(AllSelected));
        RefreshSelectionAvailability();
    }

    private void RefreshSelectionAvailability()
    {
        OnPropertyChanged(nameof(CanSelectEntries));
        OnPropertyChanged(nameof(CanActOnSelectedEntries));
        ToggleSelectAllCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Entries.CollectionChanged -= OnEntriesChanged;
        foreach (FileLockEntry entry in _observedEntries) entry.PropertyChanged -= OnEntryPropertyChanged;
        _observedEntries.Clear();
        RefreshSelectionAvailability();
        OnPropertyChanged(nameof(CanEditPath));
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }
}
