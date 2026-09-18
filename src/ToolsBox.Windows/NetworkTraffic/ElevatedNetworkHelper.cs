using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.Windows.NetworkTraffic;

public static class ElevatedNetworkHelper
{
    private const int TrafficBatchSize = 2048;
    private static readonly TimeSpan TrafficFlushInterval = TimeSpan.FromMilliseconds(250);

    public static async Task<int> RunAsync(
        string pipeName,
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(token))
        {
            return 2;
        }

        await using var stream = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            System.Security.Principal.TokenImpersonationLevel.Identification);
        await stream.ConnectAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        await using var pipe = new LengthPrefixedJsonPipe(stream, leaveOpen: true);
        await pipe.WriteAsync(NetworkHelperMessage.Create("handshake", null, new { Token = token }), cancellationToken)
            .ConfigureAwait(false);
        NetworkHelperMessage? accepted = await pipe.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (accepted is null ||
            accepted.Version != NetworkHelperProtocol.Version ||
            !string.Equals(accepted.Type, "handshake-accepted", StringComparison.Ordinal))
        {
            return 3;
        }

        var outgoing = Channel.CreateBounded<NetworkHelperMessage>(new BoundedChannelOptions(128)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task writerTask = RunWriterAsync(pipe, outgoing.Reader, lifetime.Token);
        await using var source = new EtwNetworkTrafficSource();
        var limits = new WindowsBandwidthLimitService();
        CancellationTokenSource? monitorCancellation = null;
        Task? trafficTask = null;

        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                NetworkHelperMessage? message = await pipe.ReadAsync(lifetime.Token).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                if (message.Version != NetworkHelperProtocol.Version)
                {
                    await QueueResponseAsync(outgoing.Writer, message.RequestId, NetworkHelperResponse.Failure("协议版本不兼容。"), lifetime.Token)
                        .ConfigureAwait(false);
                    continue;
                }

                if (string.Equals(message.Type, "shutdown", StringComparison.Ordinal))
                {
                    await QueueResponseAsync(outgoing.Writer, message.RequestId, NetworkHelperResponse.Success(new { }), lifetime.Token)
                        .ConfigureAwait(false);
                    break;
                }

                try
                {
                    switch (message.Type)
                    {
                        case "start-monitor":
                            if (monitorCancellation is null)
                            {
                                await source.StartAsync(lifetime.Token).ConfigureAwait(false);
                                monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                                trafficTask = RunTrafficPumpAsync(source, outgoing.Writer, monitorCancellation.Token);
                            }

                            await QueueResponseAsync(outgoing.Writer, message.RequestId, NetworkHelperResponse.Success(new { Started = true }), lifetime.Token)
                                .ConfigureAwait(false);
                            break;

                        case "stop-monitor":
                            await StopMonitorAsync(source, monitorCancellation, trafficTask).ConfigureAwait(false);
                            monitorCancellation = null;
                            trafficTask = null;
                            await QueueResponseAsync(outgoing.Writer, message.RequestId, NetworkHelperResponse.Success(new { Stopped = true }), lifetime.Token)
                                .ConfigureAwait(false);
                            break;

                        case "get-rules":
                            IReadOnlyList<BandwidthLimitRule> rules = await limits.GetRulesAsync(lifetime.Token).ConfigureAwait(false);
                            await QueueResponseAsync(outgoing.Writer, message.RequestId, NetworkHelperResponse.Success(rules), lifetime.Token)
                                .ConfigureAwait(false);
                            break;

                        case "set-upload-limit":
                            NetworkHelperSetLimitPayload setRequest = Deserialize<NetworkHelperSetLimitPayload>(message.Payload);
                            BandwidthLimitRule rule = await limits.SetUploadLimitAsync(
                                setRequest.ExecutablePath,
                                setRequest.BitsPerSecond,
                                lifetime.Token).ConfigureAwait(false);
                            await QueueResponseAsync(outgoing.Writer, message.RequestId, NetworkHelperResponse.Success(rule), lifetime.Token)
                                .ConfigureAwait(false);
                            break;

                        case "remove-upload-limit":
                            NetworkHelperPathPayload removeRequest = Deserialize<NetworkHelperPathPayload>(message.Payload);
                            await limits.RemoveUploadLimitAsync(removeRequest.ExecutablePath, lifetime.Token).ConfigureAwait(false);
                            await QueueResponseAsync(outgoing.Writer, message.RequestId, NetworkHelperResponse.Success(new { Removed = true }), lifetime.Token)
                                .ConfigureAwait(false);
                            break;

                        default:
                            await QueueResponseAsync(outgoing.Writer, message.RequestId, NetworkHelperResponse.Failure("未知的网络辅助进程命令。"), lifetime.Token)
                                .ConfigureAwait(false);
                            break;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    await QueueResponseAsync(outgoing.Writer, message.RequestId, NetworkHelperResponse.Failure(exception.Message), lifetime.Token)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            await StopMonitorAsync(source, monitorCancellation, trafficTask).ConfigureAwait(false);
            outgoing.Writer.TryComplete();
            try
            {
                await writerTask.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException)
            {
            }
        }

        return 0;
    }

    internal static async Task RunTrafficPumpAsync(
        INetworkTrafficSource source,
        ChannelWriter<NetworkHelperMessage> writer,
        CancellationToken cancellationToken)
    {
        var batch = new List<NetworkTrafficDelta>(TrafficBatchSize);
        await using IAsyncEnumerator<NetworkTrafficDelta> enumerator = source.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        Task<bool> moveNext = enumerator.MoveNextAsync().AsTask();
        Task delay = Task.Delay(TrafficFlushInterval, cancellationToken);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Task completed = await Task.WhenAny(moveNext, delay).ConfigureAwait(false);
                if (completed == moveNext)
                {
                    if (!await moveNext.ConfigureAwait(false))
                    {
                        break;
                    }

                    batch.Add(enumerator.Current);
                    moveNext = enumerator.MoveNextAsync().AsTask();
                    if (batch.Count < TrafficBatchSize && !delay.IsCompleted)
                    {
                        continue;
                    }
                }

                if (batch.Count > 0)
                {
                    NetworkTrafficDelta[] deltas = [.. batch];
                    batch.Clear();
                    await writer.WriteAsync(
                        NetworkHelperMessage.Create(
                            "traffic-batch",
                            null,
                            new NetworkTrafficBatchPayload(deltas, source.DroppedEventCount)),
                        cancellationToken).ConfigureAwait(false);
                }

                // Keep a fixed flush deadline; individual events must not postpone it.
                delay = Task.Delay(TrafficFlushInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            // Async iterators cannot be disposed while MoveNextAsync is still running.
            try
            {
                await moveNext.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private static async Task StopMonitorAsync(
        INetworkTrafficSource source,
        CancellationTokenSource? monitorCancellation,
        Task? trafficTask)
    {
        monitorCancellation?.Cancel();
        await source.StopAsync().ConfigureAwait(false);
        if (trafficTask is not null)
        {
            try
            {
                await trafficTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        monitorCancellation?.Dispose();
    }

    private static async Task RunWriterAsync(
        LengthPrefixedJsonPipe pipe,
        ChannelReader<NetworkHelperMessage> reader,
        CancellationToken cancellationToken)
    {
        await foreach (NetworkHelperMessage message in reader.ReadAllAsync(cancellationToken))
        {
            await pipe.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    private static ValueTask QueueResponseAsync(
        ChannelWriter<NetworkHelperMessage> writer,
        string? requestId,
        NetworkHelperResponse response,
        CancellationToken cancellationToken) =>
        writer.WriteAsync(NetworkHelperMessage.Create("response", requestId, response), cancellationToken);

    private static T Deserialize<T>(JsonElement payload) =>
        payload.Deserialize<T>(NetworkHelperProtocol.JsonOptions)
        ?? throw new InvalidDataException("网络辅助进程请求内容无效。");
}
