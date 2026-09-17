using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.Core.Tests.NetworkTraffic;

public sealed class BandwidthLimitRuleTests
{
    [Theory]
    [InlineData(128, BandwidthUnit.KilobytesPerSecond, 1_048_576UL)]
    [InlineData(1, BandwidthUnit.MegabytesPerSecond, 8_388_608UL)]
    [InlineData(5, BandwidthUnit.MegabytesPerSecond, 41_943_040UL)]
    public void ToBitsPerSecond_UsesBinaryByteUnits(
        decimal value,
        BandwidthUnit unit,
        ulong expected)
    {
        Assert.Equal(expected, BandwidthLimitRule.ToBitsPerSecond(value, unit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToBitsPerSecond_RejectsNonPositiveValues(decimal value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BandwidthLimitRule.ToBitsPerSecond(value, BandwidthUnit.KilobytesPerSecond));
    }

    [Fact]
    public void ToBitsPerSecond_RejectsValuesBeyondUInt64Range()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BandwidthLimitRule.ToBitsPerSecond(decimal.MaxValue, BandwidthUnit.MegabytesPerSecond));
    }
}
