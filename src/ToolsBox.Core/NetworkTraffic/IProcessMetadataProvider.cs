namespace ToolsBox.Core.NetworkTraffic;

public interface IProcessMetadataProvider
{
    ValueTask<ProcessMetadata> GetAsync(
        int processId,
        DateTimeOffset? knownStartTime,
        CancellationToken cancellationToken = default);
}
