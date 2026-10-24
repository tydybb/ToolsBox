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

    private static readonly PortEntry[] FieldEntries =
    [
        new("TCP", AddressFamily.InterNetwork, "127.0.0.1", 80, "10.0.0.8", 443, "Established", 123, "worker80"),
        new("TCP", AddressFamily.InterNetwork, "10.0.0.1", 8080, "10.0.0.2", 80, "Listen", 1234, "123"),
        new("UDP", AddressFamily.InterNetworkV6, "fe80::1%12", 5353, "", null, "", 80, "svchost"),
        new("TCP", AddressFamily.InterNetworkV6, "::1", 123, "fe80::a%4", 8080, "Established", 42, "tcp"),
        new("UDP", AddressFamily.InterNetwork, "0.0.0.0", 0, "", null, "", 0, "zero"),
        new("TCP", AddressFamily.InterNetwork, "10.1.2.3", 65535, "10.1.2.4", 65535, "Established", int.MaxValue, "maximum")
    ];

    [Fact]
    public void Apply_DeclaresOptionalSearchFieldDefaultingToAll()
    {
        System.Reflection.MethodInfo method = Assert.Single(typeof(PortEntryFilter).GetMethods()
            .Where(method => method.Name == nameof(PortEntryFilter.Apply)
                && method.GetParameters().Length == 3));
        System.Reflection.ParameterInfo field = method.GetParameters()[2];

        Assert.Equal("ToolsBox.Core.Ports.PortSearchField", field.ParameterType.FullName);
        Assert.True(field.HasDefaultValue);
        Assert.Equal("All", field.DefaultValue?.ToString());
        Assert.Equal(new[] { "All", "Port", "IpAddress", "ProcessId", "ProcessName" },
            Enum.GetNames(field.ParameterType));
    }

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
    [InlineData("234", 1234)]
    [InlineData("080", 1234)]
    [InlineData("443", 1234)]
    [InlineData("  DOTNET  ", 1234)]
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

    [Fact]
    public void Apply_WithExplicitAllField_PreservesTheExistingBroadSearch()
    {
        Assert.Equal(FieldEntries.Take(4), PortEntryFilter.Apply(FieldEntries, "80", PortSearchField.All));
    }

    [Theory]
    [InlineData(PortSearchField.All)]
    [InlineData(PortSearchField.Port)]
    [InlineData(PortSearchField.IpAddress)]
    [InlineData(PortSearchField.ProcessId)]
    [InlineData(PortSearchField.ProcessName)]
    public void Apply_WithBlankQueryInAnyField_PreservesEveryEntryInInputOrder(PortSearchField field)
    {
        foreach (string? query in new string?[] { null, string.Empty, " \t\r\n " })
        {
            Assert.Equal(FieldEntries, PortEntryFilter.Apply(FieldEntries, query, field));
        }
    }

    [Theory]
    [InlineData("80")]
    [InlineData("00080")]
    [InlineData(" 80 ")]
    public void Apply_WithPortField_MatchesExactLocalAndRemotePortWithoutMatchingPid(string query)
    {
        Assert.Equal(FieldEntries.Take(2), PortEntryFilter.Apply(FieldEntries, query, PortSearchField.Port));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("00000", 0)]
    [InlineData("65535", int.MaxValue)]
    public void Apply_WithPortField_AllowsPortBoundaryValues(string query, int expectedPid)
    {
        Assert.Equal(expectedPid, Assert.Single(PortEntryFilter.Apply(FieldEntries, query, PortSearchField.Port)).ProcessId);
    }

    [Fact]
    public void Apply_WithPortField_MatchesUdpLocalPortWithNoRemotePort()
    {
        Assert.Equal(FieldEntries[2], Assert.Single(PortEntryFilter.Apply(FieldEntries, "5353", PortSearchField.Port)));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("+80")]
    [InlineData("8 0")]
    [InlineData("8\t0")]
    [InlineData("65536")]
    [InlineData("2147483648")]
    [InlineData("80.0")]
    [InlineData("0x50")]
    [InlineData("８０")]
    [InlineData("TCP")]
    public void Apply_WithPortFieldAndInvalidNumber_ReturnsNoEntries(string query)
    {
        Assert.Empty(PortEntryFilter.Apply(FieldEntries, query, PortSearchField.Port));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("00123")]
    [InlineData(" 123 ")]
    public void Apply_WithProcessIdField_MatchesExactPidWithoutMatchingPortOrLongerPid(string query)
    {
        Assert.Equal(FieldEntries[0], Assert.Single(PortEntryFilter.Apply(FieldEntries, query, PortSearchField.ProcessId)));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("0000", 0)]
    [InlineData("2147483647", int.MaxValue)]
    public void Apply_WithProcessIdField_AllowsNonNegativeIntBoundaries(string query, int expectedPid)
    {
        Assert.Equal(expectedPid, Assert.Single(PortEntryFilter.Apply(FieldEntries, query, PortSearchField.ProcessId)).ProcessId);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("+123")]
    [InlineData("1 23")]
    [InlineData("2147483648")]
    [InlineData("123.0")]
    [InlineData("dotnet")]
    public void Apply_WithProcessIdFieldAndInvalidNumber_ReturnsNoEntries(string query)
    {
        Assert.Empty(PortEntryFilter.Apply(FieldEntries, query, PortSearchField.ProcessId));
    }

    [Theory]
    [InlineData("FE80")]
    [InlineData(" fe80 ")]
    public void Apply_WithIpAddressField_MatchesLocalAndRemoteIpv6IgnoringCase(string query)
    {
        Assert.Equal(new[] { FieldEntries[2], FieldEntries[3] },
            PortEntryFilter.Apply(FieldEntries, query, PortSearchField.IpAddress));
    }

    [Fact]
    public void Apply_WithIpAddressField_MatchesIpv6ScopeSuffix()
    {
        Assert.Equal(FieldEntries[2], Assert.Single(PortEntryFilter.Apply(FieldEntries, "%12", PortSearchField.IpAddress)));
    }

    [Theory]
    [InlineData("127.0.0", 123)]
    [InlineData("10.0.0.2", 1234)]
    public void Apply_WithIpAddressField_MatchesIpv4AddressSubstring(string query, int expectedPid)
    {
        Assert.Equal(expectedPid, Assert.Single(PortEntryFilter.Apply(FieldEntries, query, PortSearchField.IpAddress)).ProcessId);
    }

    [Theory]
    [InlineData("IPv6")]
    [InlineData("UDP")]
    [InlineData("Established")]
    [InlineData("5353")]
    [InlineData("1234")]
    [InlineData("svchost")]
    public void Apply_WithIpAddressField_DoesNotSearchOtherFields(string query)
    {
        Assert.Empty(PortEntryFilter.Apply(FieldEntries, query, PortSearchField.IpAddress));
    }

    [Theory]
    [InlineData("WORKER", 123)]
    [InlineData("  SVCHOST  ", 80)]
    [InlineData("123", 1234)]
    [InlineData("TCP", 42)]
    [InlineData("80", 123)]
    public void Apply_WithProcessNameField_MatchesOnlyNameSubstringsIgnoringCase(string query, int expectedPid)
    {
        Assert.Equal(expectedPid, Assert.Single(PortEntryFilter.Apply(FieldEntries, query, PortSearchField.ProcessName)).ProcessId);
    }

    [Theory]
    [InlineData("Established")]
    [InlineData("UDP")]
    [InlineData("fe80")]
    [InlineData("443")]
    [InlineData("42")]
    public void Apply_WithProcessNameField_DoesNotSearchOtherFields(string query)
    {
        Assert.Empty(PortEntryFilter.Apply(FieldEntries, query, PortSearchField.ProcessName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("80")]
    public void Apply_WithUnknownField_ThrowsArgumentOutOfRangeException(string? query)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PortEntryFilter.Apply(FieldEntries, query, (PortSearchField)99));
    }
}
