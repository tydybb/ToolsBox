using System.Buffers.Binary;
using System.Text.Json;

namespace ToolsBox.Windows.NetworkTraffic;

public sealed class LengthPrefixedJsonPipe : IAsyncDisposable
{
    public const int MaximumFrameBytes = 1024 * 1024;

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public LengthPrefixedJsonPipe(Stream stream, bool leaveOpen = false)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    public async ValueTask WriteAsync(
        NetworkHelperMessage message,
        CancellationToken cancellationToken = default)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, NetworkHelperProtocol.JsonOptions);
        if (payload.Length > MaximumFrameBytes)
        {
            throw new InvalidDataException("网络辅助进程消息超过大小限制。");
        }

        byte[] prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask<NetworkHelperMessage?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        byte[] prefix = new byte[sizeof(int)];
        int firstRead = await _stream.ReadAsync(prefix.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if (firstRead == 0)
        {
            return null;
        }

        await _stream.ReadExactlyAsync(prefix.AsMemory(1), cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > MaximumFrameBytes)
        {
            throw new InvalidDataException("网络辅助进程消息长度无效。");
        }

        byte[] payload = new byte[length];
        await _stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<NetworkHelperMessage>(payload, NetworkHelperProtocol.JsonOptions)
               ?? throw new InvalidDataException("网络辅助进程消息内容无效。");
    }

    public async ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        if (!_leaveOpen)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
