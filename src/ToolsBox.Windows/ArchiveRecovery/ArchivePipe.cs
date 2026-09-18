using System.Buffers.Binary;
using System.Text.Json;

namespace ToolsBox.Windows.ArchiveRecovery;

internal static class ArchivePipe
{
    internal const int MaximumFrameBytes = 64 * 1024;
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        byte[] prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 0 or > MaximumFrameBytes) throw new InvalidDataException("辅助进程消息长度无效。");
        byte[] payload = new byte[length];
        try
        {
            await stream.ReadExactlyAsync(payload, token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(payload) ?? throw new InvalidDataException("辅助进程消息无效。");
        }
        finally { Array.Clear(payload); }
    }
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value);
        try
        {
            if (payload.Length > MaximumFrameBytes) throw new InvalidDataException("辅助进程消息过长。");
            byte[] prefix = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
            await stream.WriteAsync(prefix, token).ConfigureAwait(false);
            await stream.WriteAsync(payload, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        finally { Array.Clear(payload); }
    }
}
