using System.Threading.Channels;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.Windows.NetworkTraffic;

public sealed class EtwNetworkTrafficSource : INetworkTrafficSource
{
    private const int QueueCapacity = 65_536;
    private readonly object _syncRoot = new();
    private TraceEventSession? _session;
    private Channel<NetworkTrafficDelta>? _channel;
    private Task? _processingTask;
    private long _droppedEventCount;

    public long DroppedEventCount => Interlocked.Read(ref _droppedEventCount);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            if (_session is not null)
            {
                return Task.CompletedTask;
            }

            if (TraceEventSession.IsElevated() != true)
            {
                throw new UnauthorizedAccessException("网络监控需要管理员权限。");
            }

            Interlocked.Exchange(ref _droppedEventCount, 0);
            _channel = Channel.CreateBounded<NetworkTrafficDelta>(new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            string sessionName = $"BaoGeToolsBox-Network-{Environment.ProcessId}-{Guid.NewGuid():N}";
            var session = new TraceEventSession(sessionName) { StopOnDispose = true };
            try
            {
                InitializeSession(
                    () => session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP),
                    () => Subscribe(session));
                _session = session;
                _processingTask = Task.Run(() => session.Source.Process(), CancellationToken.None);
            }
            catch
            {
                session.Dispose();
                _channel.Writer.TryComplete();
                _channel = null;
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<NetworkTrafficDelta> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Channel<NetworkTrafficDelta> channel;
        lock (_syncRoot)
        {
            channel = _channel ?? throw new InvalidOperationException("网络监控尚未启动。");
        }

        await foreach (NetworkTrafficDelta delta in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return delta;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        TraceEventSession? session;
        Channel<NetworkTrafficDelta>? channel;
        Task? processingTask;
        lock (_syncRoot)
        {
            session = _session;
            channel = _channel;
            processingTask = _processingTask;
            _session = null;
            _channel = null;
            _processingTask = null;
        }

        if (session is null)
        {
            return;
        }

        session.Source.StopProcessing();
        session.Dispose();
        channel?.Writer.TryComplete();
        if (processingTask is not null)
        {
            await processingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    internal static void InitializeSession(Action enableKernelProvider, Action subscribe)
    {
        enableKernelProvider();
        subscribe();
    }

    private void Subscribe(TraceEventSession session)
    {
        session.Source.Kernel.TcpIpSend += data => Publish(data.ProcessID, NetworkTrafficDirection.Upload, data.size, data.TimeStamp);
        session.Source.Kernel.TcpIpRecv += data => Publish(data.ProcessID, NetworkTrafficDirection.Download, data.size, data.TimeStamp);
        session.Source.Kernel.TcpIpSendIPV6 += data => Publish(data.ProcessID, NetworkTrafficDirection.Upload, data.size, data.TimeStamp);
        session.Source.Kernel.TcpIpRecvIPV6 += data => Publish(data.ProcessID, NetworkTrafficDirection.Download, data.size, data.TimeStamp);
        session.Source.Kernel.UdpIpSend += data => Publish(data.ProcessID, NetworkTrafficDirection.Upload, data.size, data.TimeStamp);
        session.Source.Kernel.UdpIpRecv += data => Publish(data.ProcessID, NetworkTrafficDirection.Download, data.size, data.TimeStamp);
        session.Source.Kernel.UdpIpSendIPV6 += data => Publish(data.ProcessID, NetworkTrafficDirection.Upload, data.size, data.TimeStamp);
        session.Source.Kernel.UdpIpRecvIPV6 += data => Publish(data.ProcessID, NetworkTrafficDirection.Download, data.size, data.TimeStamp);
    }

    private void Publish(int processId, NetworkTrafficDirection direction, int size, DateTime timestamp)
    {
        if (processId <= 0 || size <= 0)
        {
            return;
        }

        Channel<NetworkTrafficDelta>? channel = _channel;
        var delta = new NetworkTrafficDelta(
            processId,
            null,
            direction,
            size,
            new DateTimeOffset(timestamp).ToUniversalTime());
        if (channel is null || !channel.Writer.TryWrite(delta))
        {
            Interlocked.Increment(ref _droppedEventCount);
        }
    }
}
