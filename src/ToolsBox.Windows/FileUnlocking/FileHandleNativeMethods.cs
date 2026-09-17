using System.Runtime.InteropServices;
using System.Text;

namespace ToolsBox.Windows.FileUnlocking;

internal static class FileHandleNativeMethods
{
    internal const int SystemExtendedHandleInformation = 64;
    internal const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    internal const uint ProcessDuplicateHandle = 0x0040;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint DuplicateCloseSource = 0x00000001;
    internal const uint DuplicateSameAccess = 0x00000002;
    internal const uint FileTypeDisk = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemHandleTableEntryInfoEx
    {
        internal IntPtr Object;
        internal UIntPtr UniqueProcessId;
        internal UIntPtr HandleValue;
        internal uint GrantedAccess;
        internal ushort CreatorBackTraceIndex;
        internal ushort ObjectTypeIndex;
        internal uint HandleAttributes;
        internal uint Reserved;
    }

    [DllImport("ntdll.dll")]
    internal static extern int NtQuerySystemInformation(int informationClass, IntPtr information, int informationLength, ref int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess,
        out IntPtr targetHandle, uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint options);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint GetFinalPathNameByHandle(IntPtr file, StringBuilder path, uint pathLength, uint flags);

    [DllImport("kernel32.dll")]
    internal static extern uint GetFileType(IntPtr file);
}
