using System.IO.Pipes;
using ToolsBox.Core.ArchiveRecovery;

namespace ToolsBox.Windows.ArchiveRecovery;

public sealed class ArchiveRecoveryClient(string executable) : IArchivePasswordVerifier, IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private NamedPipeServerStream? _pipe;
    private ArchiveWorkerProcess? _worker;
    private bool _disposed;

    public async Task<ArchivePasswordResult> VerifyAsync(string path, string password, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromSeconds(300))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _serial.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            linked.CancelAfter(timeout);
            var pipe = await EnsureStartedAsync(linked.Token).ConfigureAwait(false);
            await ArchivePipe.WriteAsync(pipe, new ArchiveVerificationRequest(Path.GetFullPath(path), password), linked.Token).ConfigureAwait(false);
            return await ArchivePipe.ReadAsync<ArchivePasswordResult>(pipe, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            StopWorker();
            return new(ArchivePasswordOutcome.Inconclusive, "单个候选验证超时，任务已停止；可以增加验证时限后重试。");
        }
        catch (OperationCanceledException) { StopWorker(); throw; }
        catch
        {
            StopWorker();
            linked.Token.ThrowIfCancellationRequested();
            return new(ArchivePasswordOutcome.Inconclusive, "辅助进程启动或验证失败（可能是权限、资源限制或进程退出），任务已停止。");
        }
        finally { _serial.Release(); }
    }

    private async Task<NamedPipeServerStream> EnsureStartedAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        string session = Guid.NewGuid().ToString("N");
        NamedPipeServerStream pipe;
        ArchiveWorkerProcess worker;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pipe is not null) return _pipe;
            string pipeName = "ToolsBox.Archive." + session;
            pipe = ArchivePipeSecurity.Create(pipeName);
            _pipe = pipe;
            // Debug/test callers may identify the managed assembly. Prefer its Windows apphost,
            // just like the shipped EXE: a restricted console dotnet host can fail initialization
            // when its parent is elevated, before managed helper dispatch runs.
            string host = Path.GetFullPath(executable);
            if (Path.GetExtension(host).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                host = Path.ChangeExtension(host, ".exe");
            var arguments = new List<string>();
            arguments.AddRange(["--archive-worker", pipeName, session, Environment.ProcessId.ToString()]);
            worker = ArchiveWorkerProcess.Start(host, arguments.ToArray());
            _worker = worker;
        }
        await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
        if (!ArchiveRecoveryWorker.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint peer) || peer != worker.Id)
            throw new IOException("辅助进程身份验证失败。");
        string hello = await ArchivePipe.ReadAsync<string>(pipe, token).ConfigureAwait(false);
        if (hello != session) throw new IOException("辅助进程会话验证失败。");
        return pipe;
    }

    private void StopWorker()
    {
        lock (_sync)
        {
            _pipe?.Dispose(); _pipe = null;
            _worker?.Dispose(); _worker = null;
        }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            StopWorker();
        }
    }
}

internal sealed record ArchiveVerificationRequest(string Path, string Password);
