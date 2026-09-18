using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace ToolsBox.Windows.ArchiveRecovery;

internal static class ArchivePipeSecurity
{
    public static NamedPipeServerStream Create(string name)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        // CurrentUserOnly compares elevation too. Use the exact user SID across integrity levels.
        using var identity = WindowsIdentity.GetCurrent();
        string userSid = identity.User?.Value ?? throw new IOException("无法确认当前用户。");
        var security = new PipeSecurity();
        security.SetSecurityDescriptorSddlForm($"O:{userSid}D:P(A;;GA;;;{userSid})");
        var pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, security, HandleInheritability.None, PipeAccessRights.TakeOwnership);
        try
        {
            // Set only LABEL_SECURITY_INFORMATION, not the whole SACL (which requires SeSecurity).
            // WRITE_OWNER on our own pipe suffices to lower its label to the worker's medium level.
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor("S:(ML;;NW;;;ME)", 1, out IntPtr descriptor, out _)) throw new Win32Exception();
            try
            {
                if (!GetSecurityDescriptorSacl(descriptor, out bool present, out IntPtr sacl, out _) || !present) throw new Win32Exception();
                uint error = SetSecurityInfo(pipe.SafePipeHandle, 6, 0x10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, sacl);
                if (error != 0) throw new Win32Exception((int)error);
            }
            finally { LocalFree(descriptor); }
            return pipe;
        }
        catch { pipe.Dispose(); throw; }
    }
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSecurityDescriptorSacl(IntPtr descriptor, [MarshalAs(UnmanagedType.Bool)] out bool present, out IntPtr sacl, [MarshalAs(UnmanagedType.Bool)] out bool defaulted);
    [DllImport("advapi32.dll")] private static extern uint SetSecurityInfo(SafePipeHandle handle, int objectType, uint information, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
