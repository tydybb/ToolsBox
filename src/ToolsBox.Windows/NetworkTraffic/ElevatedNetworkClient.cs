using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.Windows.NetworkTraffic;

public sealed class ElevatedNetworkClient : INetworkTrafficSource, IBandwidthLimitService
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<NetworkHelperResponse>> _pending = new();
    private readonly Channel<NetworkTrafficDelta> _traffic = Channel.CreateBounded<NetworkTrafficDelta>(
        new BoundedChannelOptions(65_536)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly CancellationTokenSource _lifetime = new();
    private LengthPrefixedJsonPipe? _pipe;
    private Process? _helperProcess;
    private Task? _readLoop;
    private long _droppedEventCount;
    private bool _disposed;

    public long DroppedEventCount => Interlocked.Read(ref _droppedEventCount);
    public BandwidthDirection SupportedDirections => BandwidthDirection.Upload;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _ = await SendCommandAsync<JsonElement>("start-monitor", new { }, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<NetworkTrafficDelta> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (NetworkTrafficDelta delta in _traffic.Reader.ReadAllAsync(cancellationToken))
        {
            yield return delta;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_pipe is not null)
        {
            _ = await SendCommandAsync<JsonElement>("stop-monitor", new { }, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<IReadOnlyList<BandwidthLimitRule>> GetRulesAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync<IReadOnlyList<BandwidthLimitRule>>("get-rules", new { }, cancellationToken);

    public Task<BandwidthLimitRule> SetUploadLimitAsync(
        string executablePath,
        ulong bitsPerSecond,
        CancellationToken cancellationToken = default) =>
        SendCommandAsync<BandwidthLimitRule>(
            "set-upload-limit",
            new NetworkHelperSetLimitPayload(executablePath, bitsPerSecond),
            cancellationToken);

    public async Task RemoveUploadLimitAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        _ = await SendCommandAsync<JsonElement>(
            "remove-upload-limit",
            new NetworkHelperPathPayload(executablePath),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_pipe is not null)
            {
                _ = await SendCommandAsync<JsonElement>("shutdown", new { }, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        _lifetime.Cancel();
        _traffic.Writer.TryComplete();
        LengthPrefixedJsonPipe? pipe = Interlocked.Exchange(ref _pipe, null);
        if (pipe is not null)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }

        _helperProcess?.Dispose();
        _connectionLock.Dispose();
        _lifetime.Dispose();
    }

    private async Task<T> SendCommandAsync<T>(
        string type,
        object payload,
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        string requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<NetworkHelperResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("无法创建网络辅助进程请求。");
        }

        try
        {
            LengthPrefixedJsonPipe pipe = _pipe ?? throw new IOException("网络辅助进程连接已断开。");
            await pipe.WriteAsync(NetworkHelperMessage.Create(type, requestId, payload), cancellationToken).ConfigureAwait(false);
            NetworkHelperResponse response = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!response.Succeeded)
            {
                throw new InvalidOperationException(response.Error ?? "网络辅助进程操作失败。");
            }

            if (typeof(T) == typeof(JsonElement))
            {
                return (T)(object)response.Data;
            }

            return response.Data.Deserialize<T>(NetworkHelperProtocol.JsonOptions)
                   ?? throw new InvalidDataException("网络辅助进程响应内容无效。");
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pipe is not null)
        {
            return;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pipe is not null)
            {
                return;
            }

            string pipeName = $"BaoGeToolsBox.Network.{Guid.NewGuid():N}";
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                _helperProcess = StartElevatedHelper(pipeName, token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                await server.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
                var pipe = new LengthPrefixedJsonPipe(server);
                NetworkHelperMessage? handshake = await pipe.ReadAsync(timeout.Token).ConfigureAwait(false);
                string? handshakeError = null;
                if (handshake is null || !NetworkHelperProtocol.ValidateHandshake(handshake, token, out handshakeError))
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    throw new UnauthorizedAccessException(handshakeError ?? "网络辅助进程连接失败。");
                }

                await pipe.WriteAsync(NetworkHelperMessage.Create("handshake-accepted", null, new { Accepted = true }), timeout.Token)
                    .ConfigureAwait(false);
                _pipe = pipe;
                _readLoop = RunReadLoopAsync(pipe, _lifetime.Token);
            }
            catch
            {
                await server.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task RunReadLoopAsync(LengthPrefixedJsonPipe pipe, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NetworkHelperMessage? message = await pipe.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    throw new IOException("网络辅助进程连接已关闭。");
                }

                if (message.Version != NetworkHelperProtocol.Version)
                {
                    throw new InvalidDataException("网络辅助进程协议版本不兼容。");
                }

                if (string.Equals(message.Type, "response", StringComparison.Ordinal) && message.RequestId is not null)
                {
                    if (_pending.TryGetValue(message.RequestId, out TaskCompletionSource<NetworkHelperResponse>? completion))
                    {
                        NetworkHelperResponse response = message.Payload.Deserialize<NetworkHelperResponse>(NetworkHelperProtocol.JsonOptions)
                            ?? NetworkHelperResponse.Failure("网络辅助进程响应无效。");
                        completion.TrySetResult(response);
                    }
                }
                else if (string.Equals(message.Type, "traffic-batch", StringComparison.Ordinal))
                {
                    NetworkTrafficBatchPayload batch = message.Payload.Deserialize<NetworkTrafficBatchPayload>(NetworkHelperProtocol.JsonOptions)
                        ?? throw new InvalidDataException("网络流量批次无效。");
                    Interlocked.Exchange(ref _droppedEventCount, batch.DroppedEventCount);
                    foreach (NetworkTrafficDelta delta in batch.Deltas)
                    {
                        if (!_traffic.Writer.TryWrite(delta))
                        {
                            Interlocked.Increment(ref _droppedEventCount);
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            failure = exception;
        }
        finally
        {
            foreach (TaskCompletionSource<NetworkHelperResponse> completion in _pending.Values)
            {
                completion.TrySetException(failure ?? new IOException("网络辅助进程连接已断开。"));
            }

            if (ReferenceEquals(_pipe, pipe))
            {
                _pipe = null;
            }
        }
    }

    private static Process StartElevatedHelper(string pipeName, string token)
    {
        string executablePath = Environment.ProcessPath
                                ?? throw new InvalidOperationException("无法确定宝哥工具箱程序路径。");
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--elevated-network");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add(token);
        try
        {
            return Process.Start(startInfo)
                   ?? throw new InvalidOperationException("无法启动管理员网络辅助进程。");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("已取消管理员授权。", exception);
        }
    }
}
