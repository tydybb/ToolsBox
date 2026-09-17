using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using ToolsBox.Core.Ports;

namespace ToolsBox.Windows.Ports;

public sealed class WindowsPortSnapshotProvider : IPortSnapshotProvider
{
    public Task<IReadOnlyList<PortEntry>> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("端口监控仅支持 Windows。");
        }

        return Task.Run(() => ReadSnapshot(cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<PortEntry> ReadSnapshot(CancellationToken cancellationToken)
    {
        var entries = new List<PortEntry>();
        ReadTcp4(entries, cancellationToken);
        ReadTcp6(entries, cancellationToken);
        ReadUdp4(entries, cancellationToken);
        ReadUdp6(entries, cancellationToken);

        return entries
            .OrderBy(entry => entry.LocalPort)
            .ThenBy(entry => entry.Protocol, StringComparer.Ordinal)
            .ThenBy(entry => entry.ProcessId)
            .ToArray();
    }

    private static void ReadTcp4(List<PortEntry> entries, CancellationToken cancellationToken)
    {
        WithTcpTable(NativeMethods.AfInet, (pointer, count) =>
        {
            int rowSize = Marshal.SizeOf<NativeMethods.TcpRowOwnerPid>();
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = Marshal.PtrToStructure<NativeMethods.TcpRowOwnerPid>(pointer + index * rowSize);
                entries.Add(new PortEntry(
                    "TCP", AddressFamily.InterNetwork,
                    new IPAddress(row.LocalAddress).ToString(), ConvertPort(row.LocalPort),
                    new IPAddress(row.RemoteAddress).ToString(), ConvertPort(row.RemotePort),
                    GetTcpState(row.State), (int)row.OwningPid, GetProcessName((int)row.OwningPid)));
            }
        });
    }

    private static void ReadTcp6(List<PortEntry> entries, CancellationToken cancellationToken)
    {
        WithTcpTable(NativeMethods.AfInet6, (pointer, count) =>
        {
            int rowSize = Marshal.SizeOf<NativeMethods.Tcp6RowOwnerPid>();
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = Marshal.PtrToStructure<NativeMethods.Tcp6RowOwnerPid>(pointer + index * rowSize);
                entries.Add(new PortEntry(
                    "TCP", AddressFamily.InterNetworkV6,
                    new IPAddress(row.LocalAddress, row.LocalScopeId).ToString(), ConvertPort(row.LocalPort),
                    new IPAddress(row.RemoteAddress, row.RemoteScopeId).ToString(), ConvertPort(row.RemotePort),
                    GetTcpState(row.State), (int)row.OwningPid, GetProcessName((int)row.OwningPid)));
            }
        });
    }

    private static void ReadUdp4(List<PortEntry> entries, CancellationToken cancellationToken)
    {
        WithUdpTable(NativeMethods.AfInet, (pointer, count) =>
        {
            int rowSize = Marshal.SizeOf<NativeMethods.UdpRowOwnerPid>();
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = Marshal.PtrToStructure<NativeMethods.UdpRowOwnerPid>(pointer + index * rowSize);
                entries.Add(new PortEntry(
                    "UDP", AddressFamily.InterNetwork,
                    new IPAddress(row.LocalAddress).ToString(), ConvertPort(row.LocalPort),
                    string.Empty, null, string.Empty, (int)row.OwningPid, GetProcessName((int)row.OwningPid)));
            }
        });
    }

    private static void ReadUdp6(List<PortEntry> entries, CancellationToken cancellationToken)
    {
        WithUdpTable(NativeMethods.AfInet6, (pointer, count) =>
        {
            int rowSize = Marshal.SizeOf<NativeMethods.Udp6RowOwnerPid>();
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = Marshal.PtrToStructure<NativeMethods.Udp6RowOwnerPid>(pointer + index * rowSize);
                entries.Add(new PortEntry(
                    "UDP", AddressFamily.InterNetworkV6,
                    new IPAddress(row.LocalAddress, row.LocalScopeId).ToString(), ConvertPort(row.LocalPort),
                    string.Empty, null, string.Empty, (int)row.OwningPid, GetProcessName((int)row.OwningPid)));
            }
        });
    }

    private static void WithTcpTable(int addressFamily, Action<IntPtr, int> readRows)
    {
        int size = 0;
        uint result = NativeMethods.GetExtendedTcpTable(IntPtr.Zero, ref size, true, addressFamily, NativeMethods.TcpTableClass.OwnerPidAll, 0);
        if (result != NativeMethods.ErrorInsufficientBuffer)
        {
            throw new Win32Exception((int)result);
        }

        ReadAllocatedTable(size,
            (IntPtr buffer, ref int bufferSize) => NativeMethods.GetExtendedTcpTable(buffer, ref bufferSize, true, addressFamily, NativeMethods.TcpTableClass.OwnerPidAll, 0),
            readRows);
    }

    private static void WithUdpTable(int addressFamily, Action<IntPtr, int> readRows)
    {
        int size = 0;
        uint result = NativeMethods.GetExtendedUdpTable(IntPtr.Zero, ref size, true, addressFamily, NativeMethods.UdpTableClass.OwnerPid, 0);
        if (result != NativeMethods.ErrorInsufficientBuffer)
        {
            throw new Win32Exception((int)result);
        }

        ReadAllocatedTable(size,
            (IntPtr buffer, ref int bufferSize) => NativeMethods.GetExtendedUdpTable(buffer, ref bufferSize, true, addressFamily, NativeMethods.UdpTableClass.OwnerPid, 0),
            readRows);
    }

    private delegate uint TableReader(IntPtr buffer, ref int size);

    private static void ReadAllocatedTable(int size, TableReader reader, Action<IntPtr, int> readRows)
    {
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            uint result = reader(buffer, ref size);
            if (result != 0)
            {
                throw new Win32Exception((int)result);
            }

            int count = Marshal.ReadInt32(buffer);
            readRows(buffer + sizeof(uint), count);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int ConvertPort(uint port) => (ushort)IPAddress.NetworkToHostOrder((short)port);

    private static string GetTcpState(uint state) => Enum.IsDefined(typeof(TcpState), (int)state)
        ? ((TcpState)state).ToString()
        : $"Unknown ({state})";

    private static string GetProcessName(int processId)
    {
        if (processId == 0)
        {
            return "System Idle Process";
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "进程已退出";
        }
        catch (InvalidOperationException)
        {
            return "进程已退出";
        }
        catch (Win32Exception)
        {
            return "无法访问";
        }
    }
}
