using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ToolsBox.Windows.ArchiveRecovery;

public static class ArchiveRecoveryWorker
{
    public static async Task<int> RunAsync(string pipeName, string session, string parent)
    {
        if (!Guid.TryParseExact(session, "N", out _) || pipeName != "ToolsBox.Archive." + session || !uint.TryParse(parent, out uint parentId)) return 1;
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                System.Security.Principal.TokenImpersonationLevel.Identification);
            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await pipe.ConnectAsync(connectTimeout.Token).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint server) || server != parentId) return 1;
            await ArchivePipe.WriteAsync(pipe, session, connectTimeout.Token).ConfigureAwait(false);
            var verifier = new SevenZipPasswordVerifier();
            while (true)
            {
                // Parent owns process/job. Idle timeout also bounds orphaned IPC sessions.
                using var idle = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var request = await ArchivePipe.ReadAsync<ArchiveVerificationRequest>(pipe, idle.Token).ConfigureAwait(false);
                if (request.Path is null || request.Password is null || !Path.IsPathFullyQualified(request.Path)) return 1;
                var result = verifier.Verify(request.Path, request.Password);
                await ArchivePipe.WriteAsync(pipe, result, idle.Token).ConfigureAwait(false);
            }
        }
        catch { return 1; } // Never emit archive paths, passwords or native exception text.
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
