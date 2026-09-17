namespace ToolsBox.Core.NetworkTraffic;

[Flags]
public enum BandwidthDirection
{
    None = 0,
    Upload = 1,
    Download = 2
}

public enum BandwidthUnit
{
    KilobytesPerSecond,
    MegabytesPerSecond
}

public sealed record BandwidthLimitRule(
    string RuleName,
    string ExecutablePath,
    BandwidthDirection Direction,
    ulong BitsPerSecond,
    bool IsOwned,
    bool HasConflict,
    string? ConflictReason = null)
{
    public static ulong ToBitsPerSecond(decimal value, BandwidthUnit unit)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "限速值必须大于零。");
        }

        decimal bytesPerUnit = unit switch
        {
            BandwidthUnit.KilobytesPerSecond => 1024m,
            BandwidthUnit.MegabytesPerSecond => 1024m * 1024m,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
        decimal bitsPerSecond;
        try
        {
            bitsPerSecond = value * bytesPerUnit * 8m;
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "限速值超过支持范围。");
        }

        if (bitsPerSecond > ulong.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "限速值超过支持范围。");
        }

        return decimal.ToUInt64(decimal.Round(bitsPerSecond, 0, MidpointRounding.AwayFromZero));
    }
}
