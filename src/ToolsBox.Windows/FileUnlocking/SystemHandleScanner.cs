using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ToolsBox.Core.FileUnlocking;

namespace ToolsBox.Windows.FileUnlocking;

internal sealed class SystemHandleScanner
{
    public IReadOnlyList<FileLockEntry> Scan(FileLockTarget target, CancellationToken cancellationToken)
    {
        IntPtr buffer = QueryHandleTable(out long handleCount);
        var processHandles = new Dictionary<int, IntPtr>();
        var results = new List<FileLockEntry>();
        try
        {
            int entrySize = Marshal.SizeOf<FileHandleNativeMethods.SystemHandleTableEntryInfoEx>();
            IntPtr entryPointer = buffer + IntPtr.Size * 2;
            for (long index = 0; index < handleCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nativeEntry = Marshal.PtrToStructure<FileHandleNativeMethods.SystemHandleTableEntryInfoEx>(entryPointer + checked((int)(index * entrySize)));
                int processId = unchecked((int)nativeEntry.UniqueProcessId.ToUInt64());
                if (processId <= 0 || !TryGetProcessHandle(processId, processHandles, out IntPtr processHandle))
                {
                    continue;
                }

                if (!TryDuplicate(processHandle, nativeEntry.HandleValue, out IntPtr duplicate))
                {
                    continue;
                }

                try
                {
                    string? path = TryGetPath(duplicate);
                    if (path is null || !FileLockPathMatcher.Matches(target, path))
                    {
                        continue;
                    }

                    results.Add(CreateEntry(path, nativeEntry.HandleValue, processId));
                }
                finally
                {
                    FileHandleNativeMethods.CloseHandle(duplicate);
                }
            }
        }
        finally
        {
            foreach (IntPtr processHandle in processHandles.Values)
            {
                if (processHandle != IntPtr.Zero)
                {
                    FileHandleNativeMethods.CloseHandle(processHandle);
                }
            }

            Marshal.FreeHGlobal(buffer);
        }

        return results
            .DistinctBy(item => (item.ProcessId, item.HandleValue))
            .OrderBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.LockedPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string? TryGetPath(IntPtr handle)
    {
        if (FileHandleNativeMethods.GetFileType(handle) != FileHandleNativeMethods.FileTypeDisk)
        {
            return null;
        }

        var builder = new StringBuilder(512);
        uint length = FileHandleNativeMethods.GetFinalPathNameByHandle(handle, builder, (uint)builder.Capacity, 0);
        if (length == 0)
        {
            return null;
        }

        if (length >= builder.Capacity)
        {
            builder.EnsureCapacity(checked((int)length + 1));
            length = FileHandleNativeMethods.GetFinalPathNameByHandle(handle, builder, (uint)builder.Capacity, 0);
            if (length == 0 || length >= builder.Capacity)
            {
                return null;
            }
        }

        try
        {
            return FileLockPathMatcher.Normalize(builder.ToString());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    internal static bool TryDuplicate(IntPtr processHandle, UIntPtr handleValue, out IntPtr duplicate) =>
        FileHandleNativeMethods.DuplicateHandle(processHandle, (IntPtr)handleValue, FileHandleNativeMethods.GetCurrentProcess(),
            out duplicate, 0, false, FileHandleNativeMethods.DuplicateSameAccess);

    private static IntPtr QueryHandleTable(out long handleCount)
    {
        int length = 1 << 20;
        while (true)
        {
            IntPtr buffer = Marshal.AllocHGlobal(length);
            int required = 0;
            int status = FileHandleNativeMethods.NtQuerySystemInformation(
                FileHandleNativeMethods.SystemExtendedHandleInformation, buffer, length, ref required);
            if (status == 0)
            {
                handleCount = Marshal.ReadIntPtr(buffer).ToInt64();
                return buffer;
            }

            Marshal.FreeHGlobal(buffer);
            if (status != FileHandleNativeMethods.StatusInfoLengthMismatch)
            {
                throw new Win32Exception(status, $"无法读取系统句柄表，NTSTATUS=0x{status:X8}。");
            }

            length = Math.Max(checked(length * 2), required + 65536);
        }
    }

    private static bool TryGetProcessHandle(int processId, Dictionary<int, IntPtr> cache, out IntPtr handle)
    {
        if (cache.TryGetValue(processId, out handle))
        {
            return handle != IntPtr.Zero;
        }

        handle = FileHandleNativeMethods.OpenProcess(
            FileHandleNativeMethods.ProcessDuplicateHandle | FileHandleNativeMethods.ProcessQueryLimitedInformation,
            false, (uint)processId);
        cache[processId] = handle;
        return handle != IntPtr.Zero;
    }

    private static FileLockEntry CreateEntry(string path, UIntPtr handleValue, int processId)
    {
        string processName = $"PID {processId}";
        string processPath = string.Empty;
        DateTimeOffset? startedAt = null;
        bool limited = false;
        try
        {
            using Process process = Process.GetProcessById(processId);
            processName = process.ProcessName;
            startedAt = process.StartTime.ToUniversalTime();
            try
            {
                processPath = process.MainModule?.FileName ?? string.Empty;
            }
            catch (Win32Exception)
            {
                limited = true;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            limited = true;
        }

        return new FileLockEntry(path, unchecked((long)handleValue.ToUInt64()), processId, processName, processPath, startedAt, limited);
    }
}
