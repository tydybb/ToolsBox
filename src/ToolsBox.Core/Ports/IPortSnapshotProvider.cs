namespace ToolsBox.Core.Ports;

public interface IPortSnapshotProvider
{
    Task<IReadOnlyList<PortEntry>> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
