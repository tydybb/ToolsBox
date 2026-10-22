using System.Diagnostics;
using System.IO;
using ToolsBox.Core.Attendance;

namespace ToolsBox.App.Attendance;

/// <summary>Serializes reads and owns only the short-lived process it starts for each request.</summary>
public sealed class AttendanceReadClient : IDisposable
{
    private readonly SemaphoreSlim _readerSlot = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly object _processGate = new();
    private readonly string _executable;
    private readonly string? _entryAssembly;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly TimeSpan _timeout;
    private Process? _activeProcess;
    private Process? _pendingProcess;
    private int _disposed;

    public AttendanceReadClient(string? executable = null, string? entryAssembly = null,
        Func<ProcessStartInfo, Process?>? startProcess = null, TimeSpan? timeout = null)
    {
        _executable = executable ?? Environment.ProcessPath ?? throw new InvalidOperationException("Reader entry is unavailable.");
        _entryAssembly = entryAssembly;
        if (_entryAssembly is null && IsDotnet(_executable) && Environment.GetCommandLineArgs().FirstOrDefault() is { Length: > 0 } entry)
            _entryAssembly = Path.GetFullPath(entry);
        _startProcess = startProcess ?? (info => Process.Start(info));
        _timeout = timeout ?? TimeSpan.FromSeconds(20);
        if (!Path.IsPathFullyQualified(_executable) || _timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentException("Invalid reader launch configuration.");
        _lifetimeToken = _lifetime.Token;
    }

    public async Task<AttendanceStatus> CheckAsync(string? directory, DateTime now, CancellationToken token = default)
    {
        if (Volatile.Read(ref _disposed) != 0) return AttendanceReadProtocol.Failure(AttendanceReadError.Disposed);
        using var deadline = new CancellationTokenSource(_timeout);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token, _lifetimeToken);
        CancellationToken operationToken = cancelled.Token;
        AttendanceReadRequest request;
        try { request = AttendanceReadProtocol.CreateRequest(string.IsNullOrWhiteSpace(directory) ? null : directory, now); }
        catch { return AttendanceReadProtocol.Failure(AttendanceReadError.InvalidRequest); }

        bool entered = false;
        Process? process = null;
        Task? discardErrors = null;
        CancellationTokenRegistration terminate = default;
        try
        {
            await _readerSlot.WaitAsync(operationToken).ConfigureAwait(false);
            entered = true;
            operationToken.ThrowIfCancellationRequested();
            Process? pending;
            lock (_processGate) pending = _pendingProcess;
            if (pending is not null)
            {
                if (!await ReapOwnedAsync(pending, operationToken).ConfigureAwait(false))
                    return AttendanceReadProtocol.Failure(AttendanceReadError.ProcessFailed);
                ReleasePending(pending);
                operationToken.ThrowIfCancellationRequested();
            }
            process = _startProcess(CreateStartInfo()) ?? throw new InvalidOperationException();
            lock (_processGate)
            {
                if (Volatile.Read(ref _disposed) == 0) _activeProcess = process;
            }
            Process owned = process;
            terminate = operationToken.Register(() => KillOwned(owned));
            operationToken.ThrowIfCancellationRequested();
            discardErrors = DrainAsync(process.StandardError.BaseStream, operationToken);
            await AttendanceReadProtocol.WriteRequestAsync(process.StandardInput.BaseStream, request, operationToken).ConfigureAwait(false);
            process.StandardInput.Close();
            AttendanceStatus result = await AttendanceReadProtocol.ReadResponseAsync(process.StandardOutput.BaseStream, request, operationToken).ConfigureAwait(false);
            byte[] extra = new byte[1];
            if (await process.StandardOutput.BaseStream.ReadAsync(extra, operationToken).ConfigureAwait(false) != 0)
                return AttendanceReadProtocol.Failure(AttendanceReadError.InvalidResponse);
            await process.WaitForExitAsync(operationToken).ConfigureAwait(false);
            operationToken.ThrowIfCancellationRequested();
            return process.ExitCode == 0 ? result : AttendanceReadProtocol.Failure(AttendanceReadError.ProcessFailed);
        }
        catch (OperationCanceledException)
        {
            return AttendanceReadProtocol.Failure(Volatile.Read(ref _disposed) != 0 ? AttendanceReadError.Disposed :
                token.IsCancellationRequested ? AttendanceReadError.Cancelled : AttendanceReadError.TimedOut);
        }
        catch (InvalidDataException) { return AttendanceReadProtocol.Failure(AttendanceReadError.InvalidResponse); }
        catch { return AttendanceReadProtocol.Failure(AttendanceReadError.ProcessFailed); }
        finally
        {
            terminate.Dispose();
            cancelled.Cancel();
            if (process is not null)
            {
                bool exited = await ReapOwnedAsync(process, CancellationToken.None).ConfigureAwait(false);
                lock (_processGate)
                {
                    if (ReferenceEquals(_activeProcess, process)) _activeProcess = null;
                    // An unconfirmed exit must keep ownership and prevent a later overlapping reader.
                    if (!exited) _pendingProcess = process;
                }
                if (exited) process.Dispose();
            }
            if (discardErrors is not null)
            {
                try { await discardErrors.WaitAsync(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false); }
                catch { }
            }
            if (entered) _readerSlot.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        Process? process;
        Process? pending;
        lock (_processGate) { process = _activeProcess; pending = _pendingProcess; }
        if (process is not null) KillOwned(process);
        if (pending is not null)
        {
            KillOwned(pending);
            try { if (pending.WaitForExit(1000)) ReleasePending(pending); }
            catch { }
        }
        // Calls already waiting on the slot still own their release; do not dispose the semaphore here.
        _lifetime.Dispose();
    }

    private ProcessStartInfo CreateStartInfo()
    {
        var info = new ProcessStartInfo(_executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (IsDotnet(_executable))
        {
            if (string.IsNullOrWhiteSpace(_entryAssembly) || !Path.IsPathFullyQualified(_entryAssembly) ||
                !string.Equals(Path.GetExtension(_entryAssembly), ".dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
            info.ArgumentList.Add(_entryAssembly);
        }
        info.ArgumentList.Add("--attendance-read-worker");
        return info;
    }

    private static async Task DrainAsync(Stream stream, CancellationToken token)
    {
        byte[] discard = new byte[4096];
        try { while (await stream.ReadAsync(discard, token).ConfigureAwait(false) != 0) { } }
        catch { }
    }

    private static void KillOwned(Process process)
    {
        try { if (!process.HasExited) process.Kill(); }
        catch { }
    }

    private static bool IsDotnet(string executable)
        => string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> ReapOwnedAsync(Process process, CancellationToken token)
    {
        KillOwned(process);
        using var reapDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        reapDeadline.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            await process.WaitForExitAsync(reapDeadline.Token).ConfigureAwait(false);
            return process.HasExited;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch
        {
            try { return process.HasExited; }
            catch { return false; }
        }
    }

    private void ReleasePending(Process process)
    {
        bool owned;
        lock (_processGate)
        {
            owned = ReferenceEquals(_pendingProcess, process);
            if (owned) _pendingProcess = null;
        }
        if (owned) process.Dispose();
    }
}
