using System.IO;
using System.Security.Principal;
using ToolsBox.Core.Attendance;

namespace ToolsBox.App.Attendance;

/// <summary>One ordinary-permission read; process exit releases even a failed CLR type initializer.</summary>
public static class AttendanceReadWorker
{
    public static async Task<int> RunAsync()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            bool elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using Stream input = Console.OpenStandardInput();
            using Stream output = Console.OpenStandardOutput();
            AttendanceReadRequest request = await AttendanceReadProtocol.ReadRequestAsync(input, timeout.Token).ConfigureAwait(false);
            byte[] extra = new byte[1];
            if (await input.ReadAsync(extra, timeout.Token).ConfigureAwait(false) != 0) return 2;
            AttendanceReadResponse response;
            if (elevated)
                response = AttendanceReadProtocol.CreateFailureResponse(request, AttendanceReadError.ElevatedProcess);
            else
                response = await ReadAsync(request, timeout.Token).ConfigureAwait(false);
            await AttendanceReadProtocol.WriteResponseAsync(output, response, timeout.Token).ConfigureAwait(false);
            return 0;
        }
        catch { return 2; }
    }

    private static async Task<AttendanceReadResponse> ReadAsync(AttendanceReadRequest request, CancellationToken token)
    {
        try { AttendanceSqliteRuntime.EnsureInitialized(); }
        catch { return AttendanceReadProtocol.CreateFailureResponse(request, AttendanceReadError.BootstrapFailed); }
        try
        {
            AttendanceStatus status = await Task.Run(() =>
            {
                var locator = request.Directory is null ? new DingTalkDataLocator() : new DingTalkDataLocator([request.Directory]);
                var checker = new DingTalkAttendanceChecker(locator);
                return checker.Check(request.Now);
            }, token).ConfigureAwait(false);
            return AttendanceReadProtocol.CreateResponse(request, status);
        }
        catch { return AttendanceReadProtocol.CreateFailureResponse(request, AttendanceReadError.ReadFailed); }
    }
}
