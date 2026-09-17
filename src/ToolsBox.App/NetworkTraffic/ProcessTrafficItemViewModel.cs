using ToolsBox.App.Infrastructure;
using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.App.NetworkTraffic;

public sealed class ProcessTrafficItemViewModel : ObservableObject
{
    private ProcessTrafficSnapshot _snapshot;

    public ProcessTrafficItemViewModel(ProcessTrafficSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public ProcessIdentity Identity => _snapshot.Identity;
    public int ProcessId => _snapshot.Identity.ProcessId;
    public string ProcessName => _snapshot.ProcessName;
    public string StartedAtText => _snapshot.Identity.StartedAt == DateTimeOffset.MinValue
        ? "未知"
        : _snapshot.Identity.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public long UploadBytesPerSecond => _snapshot.UploadBytesPerSecond;
    public long DownloadBytesPerSecond => _snapshot.DownloadBytesPerSecond;
    public long TotalUploadBytes => _snapshot.TotalUploadBytes;
    public long TotalDownloadBytes => _snapshot.TotalDownloadBytes;
    public string UploadRateText => ByteRateFormatter.FormatRate(UploadBytesPerSecond);
    public string DownloadRateText => ByteRateFormatter.FormatRate(DownloadBytesPerSecond);
    public string TotalUploadText => ByteRateFormatter.FormatBytes(TotalUploadBytes);
    public string TotalDownloadText => ByteRateFormatter.FormatBytes(TotalDownloadBytes);
    public string StateText => _snapshot.HasExited ? "已退出" : _snapshot.IsAccessible ? "运行中" : "受限";

    public void Update(ProcessTrafficSnapshot snapshot)
    {
        _snapshot = snapshot;
        OnPropertyChanged(string.Empty);
    }
}
