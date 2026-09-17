namespace ToolsBox.Core.Ports;

public sealed class PortMonitorService(IPortSnapshotProvider provider)
{
    private readonly IPortSnapshotProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private int _refreshing;

    public IReadOnlyList<PortEntry> Snapshot { get; private set; } = [];
    public string? LastError { get; private set; }
    public DateTimeOffset? LastRefreshedAt { get; private set; }
    public bool IsRefreshing => Volatile.Read(ref _refreshing) == 1;

    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return false;
        }

        try
        {
            IReadOnlyList<PortEntry> snapshot = await _provider.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            Snapshot = snapshot;
            LastError = null;
            LastRefreshedAt = DateTimeOffset.Now;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            return false;
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }
}
