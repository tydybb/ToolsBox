using System.Net.Sockets;
using ToolsBox.Core.Ports;

namespace ToolsBox.Core.Tests.Ports;

public sealed class PortEntryFilterTests
{
    private static readonly PortEntry[] Entries =
    [
        new("TCP", AddressFamily.InterNetwork, "127.0.0.1", 8080, "10.0.0.8", 443, "Established", 1234, "dotnet"),
        new("UDP", AddressFamily.InterNetworkV6, "::", 5353, "", null, "", 5678, "svchost")
    ];

    [Fact]
    public void Apply_WithBlankQuery_ReturnsEveryEntry()
    {
        Assert.Equal(Entries, PortEntryFilter.Apply(Entries, "  "));
    }

    [Theory]
    [InlineData("8080", 1234)]
    [InlineData("1234", 1234)]
    [InlineData("DOTNET", 1234)]
    [InlineData("tcp", 1234)]
    [InlineData("established", 1234)]
    [InlineData("10.0.0.8", 1234)]
    [InlineData("5353", 5678)]
    [InlineData("IPv6", 5678)]
    public void Apply_MatchesVisibleFieldsIgnoringCase(string query, int expectedPid)
    {
        PortEntry result = Assert.Single(PortEntryFilter.Apply(Entries, query));

        Assert.Equal(expectedPid, result.ProcessId);
    }

    [Fact]
    public void Apply_WithUnknownQuery_ReturnsEmptyResult()
    {
        Assert.Empty(PortEntryFilter.Apply(Entries, "not-present"));
    }
}
