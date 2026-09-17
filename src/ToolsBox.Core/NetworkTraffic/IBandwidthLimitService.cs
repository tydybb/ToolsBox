namespace ToolsBox.Core.NetworkTraffic;

public interface IBandwidthLimitService
{
    BandwidthDirection SupportedDirections { get; }

    Task<IReadOnlyList<BandwidthLimitRule>> GetRulesAsync(
        CancellationToken cancellationToken = default);

    Task<BandwidthLimitRule> SetUploadLimitAsync(
        string executablePath,
        ulong bitsPerSecond,
        CancellationToken cancellationToken = default);

    Task RemoveUploadLimitAsync(
        string executablePath,
        CancellationToken cancellationToken = default);
}
