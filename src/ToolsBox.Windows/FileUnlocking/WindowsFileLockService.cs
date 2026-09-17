using System.ComponentModel;
using System.Diagnostics;
using ToolsBox.Core.FileUnlocking;

namespace ToolsBox.Windows.FileUnlocking;

public sealed class WindowsFileLockService : IFileLockService
{
    private readonly SystemHandleScanner _scanner = new();

    public Task<IReadOnlyList<FileLockEntry>> FindLocksAsync(FileLockTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Task.Run(() => _scanner.Scan(target, cancellationToken), cancellationToken);
    }

    public Task<FileUnlockResult> CloseHandleAsync(FileLockEntry entry, CancellationToken cancellationToken = default) =>
        ExecuteWithElevationAsync("close-handle", entry, cancellationToken);

    public Task<FileUnlockResult> TerminateProcessAsync(FileLockEntry entry, CancellationToken cancellationToken = default) =>
        ExecuteWithElevationAsync("terminate-process", entry, cancellationToken);

    public static Task<FileUnlockResult> ExecuteElevatedRequestAsync(string encodedRequest, CancellationToken cancellationToken = default)
    {
        ElevatedActionRequest request = ElevatedActionRequest.Decode(encodedRequest);
        return Task.Run(() => request.Action switch
        {
            "close-handle" => CloseHandle(request.Entry),
            "terminate-process" => TerminateProcess(request.Entry),
            _ => FileUnlockResult.Failure("未知的提权操作。")
        }, cancellationToken);
    }

    private static async Task<FileUnlockResult> ExecuteWithElevationAsync(string action, FileLockEntry entry, CancellationToken cancellationToken)
    {
        FileUnlockResult localResult = await Task.Run(
            () => action == "close-handle" ? CloseHandle(entry) : TerminateProcess(entry), cancellationToken);
        if (localResult.Succeeded || !IsAccessDenied(localResult.Message))
        {
            return localResult;
        }

        return await RunElevatedAsync(new ElevatedActionRequest(action, entry), cancellationToken);
    }

    private static bool IsAccessDenied(string message) =>
        message.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("access is denied", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("access denied", StringComparison.OrdinalIgnoreCase);

    private static async Task<FileUnlockResult> RunElevatedAsync(ElevatedActionRequest request, CancellationToken cancellationToken)
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return FileUnlockResult.Failure("无法确定 ToolsBox 程序路径，不能请求管理员权限。");
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--elevated-unlock");
        startInfo.ArgumentList.Add(request.Encode());

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return FileUnlockResult.Failure("未能启动管理员操作。");
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0
                ? FileUnlockResult.Success("管理员操作已完成。")
                : FileUnlockResult.Failure("管理员操作失败，目标可能已变化或仍受系统保护。");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return FileUnlockResult.Failure("已取消管理员授权。");
        }
        catch (Win32Exception exception)
        {
            return FileUnlockResult.Failure(exception.Message);
        }
    }

    private static FileUnlockResult CloseHandle(FileLockEntry entry)
    {
        if (!FileUnlockSafetyPolicy.CanOperate(entry, Environment.ProcessId, out string reason))
        {
            return FileUnlockResult.Failure(reason);
        }

        if (!TryValidateProcess(entry, out Process? process, out reason))
        {
            return FileUnlockResult.Failure(reason);
        }

        using (Process validatedProcess = process!)
        {
            IntPtr processHandle = FileHandleNativeMethods.OpenProcess(
                FileHandleNativeMethods.ProcessDuplicateHandle | FileHandleNativeMethods.ProcessQueryLimitedInformation,
                false, (uint)entry.ProcessId);
            if (processHandle == IntPtr.Zero)
            {
                return FileUnlockResult.Failure(new Win32Exception().Message);
            }

            try
            {
                UIntPtr sourceValue = (UIntPtr)unchecked((ulong)entry.HandleValue);
                if (!SystemHandleScanner.TryDuplicate(processHandle, sourceValue, out IntPtr validationHandle))
                {
                    return FileUnlockResult.Failure("句柄已失效或无法访问。" );
                }

                try
                {
                    string? actualPath = SystemHandleScanner.TryGetPath(validationHandle);
                    if (actualPath is null || !string.Equals(FileLockPathMatcher.Normalize(entry.LockedPath), actualPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return FileUnlockResult.Failure("句柄目标已经变化，为避免误操作已取消。" );
                    }
                }
                finally
                {
                    FileHandleNativeMethods.CloseHandle(validationHandle);
                }

                bool closed = FileHandleNativeMethods.DuplicateHandle(processHandle, (IntPtr)sourceValue,
                    FileHandleNativeMethods.GetCurrentProcess(), out IntPtr duplicate, 0, false,
                    FileHandleNativeMethods.DuplicateSameAccess | FileHandleNativeMethods.DuplicateCloseSource);
                if (!closed)
                {
                    return FileUnlockResult.Failure(new Win32Exception().Message);
                }

                FileHandleNativeMethods.CloseHandle(duplicate);
                return FileUnlockResult.Success($"已关闭 {entry.ProcessName} 的文件句柄。" );
            }
            finally
            {
                FileHandleNativeMethods.CloseHandle(processHandle);
            }
        }
    }

    private static FileUnlockResult TerminateProcess(FileLockEntry entry)
    {
        if (!FileUnlockSafetyPolicy.CanOperate(entry, Environment.ProcessId, out string reason))
        {
            return FileUnlockResult.Failure(reason);
        }

        if (!TryValidateProcess(entry, out Process? process, out reason))
        {
            return FileUnlockResult.Failure(reason);
        }

        using (Process validatedProcess = process!)
        {
            try
            {
                validatedProcess.Kill(true);
                validatedProcess.WaitForExit(5000);
                return validatedProcess.HasExited
                    ? FileUnlockResult.Success($"已结束进程 {entry.ProcessName} ({entry.ProcessId})。")
                    : FileUnlockResult.Failure($"进程 {entry.ProcessName} 未在限定时间内退出。" );
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                return FileUnlockResult.Failure(exception.Message);
            }
        }
    }

    private static bool TryValidateProcess(FileLockEntry entry, out Process? process, out string reason)
    {
        try
        {
            process = Process.GetProcessById(entry.ProcessId);
            if (entry.ProcessStartedAt is not null && process.StartTime.ToUniversalTime() != entry.ProcessStartedAt.Value.UtcDateTime)
            {
                process.Dispose();
                process = null;
                reason = "原进程已退出，PID 已被其他进程复用。";
                return false;
            }

            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            process = null;
            reason = "占用进程已退出或无法访问。";
            return false;
        }
    }
}
