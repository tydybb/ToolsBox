using System.Collections.ObjectModel;
using System.Windows.Media;
using ToolsBox.App.Infrastructure;
using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.App.NetworkTraffic;

public sealed class ApplicationTrafficItemViewModel : ObservableObject
{
    private ApplicationTrafficSnapshot _snapshot;
    private BandwidthLimitRule? _limitRule;
    private bool _isExpanded;

    public ApplicationTrafficItemViewModel(
        ApplicationTrafficSnapshot snapshot,
        BandwidthLimitRule? limitRule)
    {
        _snapshot = snapshot;
        _limitRule = limitRule;
        RebuildProcesses(snapshot.Processes);
    }

    public string Key => _snapshot.Key;
    public ImageSource? Icon => ApplicationIconProvider.Get(_snapshot.ExecutablePath);
    public string ApplicationName => _snapshot.ApplicationName;
    public string ExecutablePath => _snapshot.ExecutablePath ?? "路径不可访问";
    public bool CanLimit => _snapshot.CanLimit && (_limitRule is null || !_limitRule.HasConflict);
    public long UploadBytesPerSecond => _snapshot.UploadBytesPerSecond;
    public long DownloadBytesPerSecond => _snapshot.DownloadBytesPerSecond;
    public long TotalUploadBytes => _snapshot.TotalUploadBytes;
    public long TotalDownloadBytes => _snapshot.TotalDownloadBytes;
    public int ProcessCount => _snapshot.Processes.Count;
    public string UploadRateText => ByteRateFormatter.FormatRate(UploadBytesPerSecond);
    public string DownloadRateText => ByteRateFormatter.FormatRate(DownloadBytesPerSecond);
    public string TotalUploadText => ByteRateFormatter.FormatBytes(TotalUploadBytes);
    public string TotalDownloadText => ByteRateFormatter.FormatBytes(TotalDownloadBytes);
    public string LimitText => _limitRule is null
        ? "不限速"
        : _limitRule.HasConflict && !_limitRule.IsOwned
            ? "外部策略"
            : $"上传 {ByteRateFormatter.FormatRate((long)(_limitRule.BitsPerSecond / 8))}";
    public string? LimitWarning => _limitRule?.ConflictReason;
    public ObservableCollection<ProcessTrafficItemViewModel> Processes { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public void Update(ApplicationTrafficSnapshot snapshot, BandwidthLimitRule? limitRule)
    {
        _snapshot = snapshot;
        _limitRule = limitRule;
        RebuildProcesses(snapshot.Processes);
        OnPropertyChanged(string.Empty);
    }

    private void RebuildProcesses(IReadOnlyList<ProcessTrafficSnapshot> snapshots)
    {
        Dictionary<ProcessIdentity, ProcessTrafficItemViewModel> existing = Processes.ToDictionary(item => item.Identity);
        Processes.Clear();
        foreach (ProcessTrafficSnapshot snapshot in snapshots)
        {
            if (!existing.TryGetValue(snapshot.Identity, out ProcessTrafficItemViewModel? item))
            {
                item = new ProcessTrafficItemViewModel(snapshot);
            }
            else
            {
                item.Update(snapshot);
            }

            Processes.Add(item);
        }
    }
}
