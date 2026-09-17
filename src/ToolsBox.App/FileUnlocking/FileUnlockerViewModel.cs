using System.Collections.ObjectModel;
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

    public FileUnlockerViewModel(IFileLockService service)
    {
        _service = service;
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(PathText));
    }

    public ObservableCollection<FileLockEntry> Entries { get; } = [];
    public AsyncRelayCommand ScanCommand { get; }

    public string PathText
    {
        get => _pathText;
        set
        {
            if (SetProperty(ref _pathText, value))
            {
                ScanCommand.RaiseCanExecuteChanged();
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
        PathText = path;
        await ScanAsync();
    }

    public async Task ScanAsync()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        IsBusy = true;
        StatusText = "正在扫描系统文件句柄…";
        try
        {
            FileLockTarget target = FileLockTarget.FromExistingPath(PathText);
            IReadOnlyList<FileLockEntry> locks = await _service.FindLocksAsync(target, _operationCancellation.Token);
            Entries.Clear();
            foreach (FileLockEntry entry in locks)
            {
                Entries.Add(entry);
            }

            StatusText = locks.Count == 0 ? "未发现占用，可正常操作该路径" : $"发现 {locks.Count} 个占用句柄";
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

    public async Task<string> TerminateSelectedProcessesAsync()
    {
        FileLockEntry[] selected = Entries.Where(item => item.IsSelected).DistinctBy(item => item.ProcessId).ToArray();
        if (selected.Length == 0)
        {
            return "请先勾选至少一条占用记录。";
        }

        return await ExecuteActionsAsync(selected, _service.TerminateProcessAsync);
    }

    public async Task<string> CloseSelectedHandlesAsync()
    {
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

        await ScanAsync();
        string summary = $"成功 {succeeded} 项，失败 {failures.Count} 项。";
        if (failures.Count > 0)
        {
            summary += Environment.NewLine + string.Join(Environment.NewLine, failures);
        }

        StatusText = summary;
        return summary;
    }

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }
}
