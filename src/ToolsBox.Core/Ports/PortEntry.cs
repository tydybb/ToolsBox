using System.Net.Sockets;

namespace ToolsBox.Core.Ports;

public sealed record PortEntry(
    string Protocol,
    AddressFamily AddressFamily,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int? RemotePort,
    string State,
    int ProcessId,
    string ProcessName)
{
    public string AddressFamilyDisplay => AddressFamily switch
    {
        AddressFamily.InterNetwork => "IPv4",
        AddressFamily.InterNetworkV6 => "IPv6",
        _ => AddressFamily.ToString()
    };
}
