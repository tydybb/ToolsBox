using System.Runtime.InteropServices;

namespace ToolsBox.Windows.Ports;

internal static class NativeMethods
{
    internal const int AfInet = 2;
    internal const int AfInet6 = 23;
    internal const int ErrorInsufficientBuffer = 122;

    internal enum TcpTableClass
    {
        OwnerPidAll = 5
    }

    internal enum UdpTableClass
    {
        OwnerPid = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TcpRowOwnerPid
    {
        internal uint State;
        internal uint LocalAddress;
        internal uint LocalPort;
        internal uint RemoteAddress;
        internal uint RemotePort;
        internal uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct UdpRowOwnerPid
    {
        internal uint LocalAddress;
        internal uint LocalPort;
        internal uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Tcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        internal byte[] LocalAddress;
        internal uint LocalScopeId;
        internal uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        internal byte[] RemoteAddress;
        internal uint RemoteScopeId;
        internal uint RemotePort;
        internal uint State;
        internal uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Udp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        internal byte[] LocalAddress;
        internal uint LocalScopeId;
        internal uint LocalPort;
        internal uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    internal static extern uint GetExtendedTcpTable(
        IntPtr table,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        TcpTableClass tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    internal static extern uint GetExtendedUdpTable(
        IntPtr table,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        UdpTableClass tableClass,
        uint reserved);
}
