using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ToolsBox.Core.Attendance;

/// <summary>Pins local Windows directory components while the native cache is extracted.</summary>
internal sealed class AttendanceNativeDirectoryLease : IDisposable
{
    private const uint ShareReadAndWrite = 0x00000001 | 0x00000002;
    private const uint OpenExisting = 3;
    private const uint BackupSemanticsAndOpenReparsePoint = 0x02000000 | 0x00200000;
    private const int MaximumDepth = 128;
    private List<SafeFileHandle>? _directories;

    private AttendanceNativeDirectoryLease(List<SafeFileHandle> directories) => _directories = directories;

    public static AttendanceNativeDirectoryLease Acquire(string directory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The native cache requires Windows directory leases.");
        if (!Path.IsPathFullyQualified(directory)) throw new IOException("The native cache directory must be absolute.");
        string path = Path.GetFullPath(directory);
        string root = Path.GetPathRoot(path)!;
        // The production cache is local; network/device paths have different filesystem semantics.
        if (root.Length != 3 || !char.IsLetter(root[0]) || root[1] != ':' || root[2] != Path.DirectorySeparatorChar)
            throw new IOException("The native cache must use a local Windows drive.");
        string[] components = path[root.Length..].Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (components.Length > MaximumDepth) throw new IOException("The native cache path is too deep.");

        var pinned = new List<SafeFileHandle>(components.Length + 1);
        try
        {
            pinned.Add(PinDirectory(root, createMissing: false));
            string current = root;
            foreach (string component in components)
            {
                current = Path.Combine(current, component);
                // The parent is already pinned: creating this one child cannot traverse a swapped ancestor.
                pinned.Add(PinDirectory(current, createMissing: true));
            }
            return new AttendanceNativeDirectoryLease(pinned);
        }
        catch
        {
            DisposeHandles(pinned);
            throw;
        }
    }

    public void Dispose()
    {
        List<SafeFileHandle>? pinned = Interlocked.Exchange(ref _directories, null);
        if (pinned is not null) DisposeHandles(pinned);
    }

    public static SafeFileHandle OpenRegularFile(string path)
        => OpenFile(path, desiredAccess: 0x80000000, shareMode: 0x00000001, creationDisposition: OpenExisting);

    public static SafeFileHandle OpenCoordinationFile(string path)
        => OpenFile(path, desiredAccess: 0x80000000 | 0x40000000, shareMode: 0, creationDisposition: 4);

    private static SafeFileHandle OpenFile(string path, uint desiredAccess, uint shareMode, uint creationDisposition)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The native cache requires Windows file handles.");
        if (!Path.IsPathFullyQualified(path)) throw new IOException("The native library path must be absolute.");
        // OPEN_REPARSE_POINT opens the named leaf itself. Never follow a link before verifying its identity.
        SafeFileHandle handle = CreateFile(path, desiredAccess, shareMode, IntPtr.Zero, creationDisposition,
            BackupSemanticsAndOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            var failure = new Win32Exception(error);
            throw error switch
            {
                2 => new FileNotFoundException("The native cache file is missing.", failure),
                3 => new DirectoryNotFoundException("The native cache directory is missing.", failure),
                _ => new IOException("The native cache file could not be opened.", failure)
            };
        }
        try
        {
            VerifyHandle(handle, directory: false);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle PinDirectory(string path, bool createMissing)
    {
        // Omitting FILE_SHARE_DELETE denies rename/removal of this directory until the lease ends.
        // Zero desired access lets ordinary users pin accessible parent directories without write access.
        SafeFileHandle handle = CreateFile(path, 0, ShareReadAndWrite, IntPtr.Zero, OpenExisting,
            BackupSemanticsAndOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (createMissing && error is 2 or 3)
            {
                Directory.CreateDirectory(path);
                return PinDirectory(path, createMissing: false);
            }
            throw new IOException("The native cache directory could not be pinned.", new Win32Exception(error));
        }
        try
        {
            VerifyHandle(handle, directory: true);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static void VerifyHandle(SafeFileHandle handle, bool directory)
    {
        if (!GetFileInformationByHandle(handle, out ByHandleFileInformation information))
            throw new IOException("The native cache handle could not be verified.", new Win32Exception(Marshal.GetLastWin32Error()));
        if ((information.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Reparse points are not supported in the native cache path.");
        bool isDirectory = (information.Attributes & FileAttributes.Directory) != 0;
        if (isDirectory != directory)
            throw new IOException("The native cache handle does not refer to the required file type.");
    }

    private static void DisposeHandles(List<SafeFileHandle> handles)
    {
        for (int index = handles.Count - 1; index >= 0; index--) handles[index].Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public FileAttributes Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFile(string path, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);
}
