using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ToolsBox.Windows.ArchiveRecovery;

// Native parsing never starts before the restricted token and kill-on-close job apply.
internal sealed class ArchiveWorkerProcess : IDisposable
{
    private readonly SafeFileHandle _job;
    private readonly Process _process;
    private bool _disposed;
    private ArchiveWorkerProcess(SafeFileHandle job, Process process) { _job = job; _process = process; }
    public int Id => _process.Id;
    public bool HasExited => _process.HasExited;

    public static ArchiveWorkerProcess Start(string executable, string[] arguments)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("辅助程序必须使用绝对路径。");
        using var current = Process.GetCurrentProcess();
        if (!Native.OpenProcessToken(current.Handle, 0x000F01FF, out var original)) throw new Win32Exception();
        using (original)
        {
            // LUA_TOKEN removes administrative groups; DISABLE_MAX_PRIVILEGE removes privileges.
            if (!Native.CreateRestrictedToken(original, 0x5, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero, out var token))
                throw new Win32Exception();
            using (token)
            {
                SetMediumIntegrity(token);
                var job = Native.CreateJobObject(IntPtr.Zero, null);
                if (job.IsInvalid) { job.Dispose(); throw new Win32Exception(); }
                Native.ProcessInformation info = default;
                try
                {
                    var limits = new Native.ExtendedLimits
                    {
                        Basic = new Native.BasicLimits { Flags = 0x2000 | 0x100 | 0x8, ActiveProcesses = 1 },
                        ProcessMemory = (UIntPtr)(768UL * 1024 * 1024)
                    };
                    if (!Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.ExtendedLimits>()))
                        throw new Win32Exception();
                    var startup = new Native.StartupInfo { Size = Marshal.SizeOf<Native.StartupInfo>() };
                    var command = new StringBuilder(string.Join(" ", new[] { executable }.Concat(arguments).Select(Quote)));
                    if (!Native.CreateProcessAsUser(token, executable, command, IntPtr.Zero, IntPtr.Zero, false,
                            0x08000000 | 0x00000004 /* NO_WINDOW | SUSPENDED */, IntPtr.Zero,
                            Path.GetDirectoryName(executable), ref startup, out info)) throw new Win32Exception();
                    if (!Native.AssignProcessToJobObject(job, info.Process)) throw new Win32Exception();
                    var process = Process.GetProcessById((int)info.ProcessId);
                    _ = process.Handle; // Retain identity/exit status even if the headless helper exits immediately.
                    if (Native.ResumeThread(info.Thread) == uint.MaxValue) { process.Dispose(); throw new Win32Exception(); }
                    return new ArchiveWorkerProcess(job, process);
                }
                catch
                {
                    if (info.Process != IntPtr.Zero) { Native.TerminateProcess(info.Process, 1); Native.WaitForSingleObject(info.Process, 5000); }
                    job.Dispose();
                    throw;
                }
                finally
                {
                    if (info.Thread != IntPtr.Zero) Native.CloseHandle(info.Thread);
                    if (info.Process != IntPtr.Zero) Native.CloseHandle(info.Process);
                }
            }
        }
    }

    private static void SetMediumIntegrity(SafeAccessTokenHandle token)
    {
        if (!Native.ConvertStringSidToSid("S-1-16-8192", out var sid)) throw new Win32Exception();
        try
        {
            var label = new Native.SidAndAttributes { Sid = sid, Attributes = 0x20 };
            if (!Native.SetTokenInformation(token, 25, ref label,
                    (uint)Marshal.SizeOf<Native.SidAndAttributes>() + Native.GetLengthSid(sid))) throw new Win32Exception();
        }
        finally { Native.LocalFree(sid); }
    }

    internal static string Quote(string argument)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _job.Dispose(); // Atomic termination of only this job, also when parent process dies.
        try { _process.WaitForExit(5000); }
        finally { _process.Dispose(); }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
        [StructLayout(LayoutKind.Sequential)] internal struct BasicLimits
        {
            public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
            public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct ExtendedLimits
        {
            public BasicLimits Basic; public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct StartupInfo
        {
            public int Size; public string? Reserved, Desktop, Title; public uint X, Y, XSize, YSize, XCount, YCount, Fill, Flags;
            public ushort Show, ReservedSize; public IntPtr Reserved2, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateRestrictedToken(SafeAccessTokenHandle original, uint flags, uint disableCount, IntPtr disable, uint deleteCount, IntPtr delete, uint restrictCount, IntPtr restrict, out SafeAccessTokenHandle token);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessAsUser(SafeAccessTokenHandle token, string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string? directory, ref StartupInfo startup, out ProcessInformation process);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ConvertStringSidToSid(string text, out IntPtr sid);
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetTokenInformation(SafeAccessTokenHandle token, int infoClass, ref SidAndAttributes value, uint size);
        [DllImport("advapi32.dll")] internal static extern uint GetLengthSid(IntPtr sid);
        [DllImport("kernel32.dll")] internal static extern IntPtr LocalFree(IntPtr memory);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateJobObject(IntPtr security, string? name);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref ExtendedLimits limits, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(IntPtr handle);
    }
}
