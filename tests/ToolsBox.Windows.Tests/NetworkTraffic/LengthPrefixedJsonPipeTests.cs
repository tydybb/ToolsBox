using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using ToolsBox.Windows.NetworkTraffic;

namespace ToolsBox.Windows.Tests.NetworkTraffic;

public sealed class LengthPrefixedJsonPipeTests
{
    [Fact]
    public async Task ReadAsync_ReassemblesFragmentedFrame()
    {
        NetworkHelperMessage expected = NetworkHelperMessage.Create("status", "42", new { Text = "已连接" });
        byte[] bytes = await SerializeAsync(expected);
        await using var fragmented = new FragmentedReadStream(bytes, 3);
        await using var pipe = new LengthPrefixedJsonPipe(fragmented);

        NetworkHelperMessage? actual = await pipe.ReadAsync();

        Assert.NotNull(actual);
        Assert.Equal(1, actual.Version);
        Assert.Equal("status", actual.Type);
        Assert.Equal("42", actual.RequestId);
        Assert.Equal("已连接", actual.Payload.GetProperty("Text").GetString());
    }

    [Fact]
    public async Task ReadAsync_ReturnsConsecutiveFrames()
    {
        var stream = new MemoryStream();
        await using (var writer = new LengthPrefixedJsonPipe(stream, leaveOpen: true))
        {
            await writer.WriteAsync(NetworkHelperMessage.Create("first", null, new { Value = 1 }));
            await writer.WriteAsync(NetworkHelperMessage.Create("second", null, new { Value = 2 }));
        }

        stream.Position = 0;
        await using var reader = new LengthPrefixedJsonPipe(stream);

        Assert.Equal("first", (await reader.ReadAsync())!.Type);
        Assert.Equal("second", (await reader.ReadAsync())!.Type);
        Assert.Null(await reader.ReadAsync());
    }

    [Fact]
    public async Task ReadAsync_RejectsOversizedFrame()
    {
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, LengthPrefixedJsonPipe.MaximumFrameBytes + 1);
        await using var pipe = new LengthPrefixedJsonPipe(new MemoryStream(prefix));

        await Assert.ThrowsAsync<InvalidDataException>(() => pipe.ReadAsync().AsTask());
    }

    [Fact]
    public async Task ReadAsync_RejectsTruncatedFrame()
    {
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, 20);
        await using var pipe = new LengthPrefixedJsonPipe(new MemoryStream([.. prefix, 1, 2, 3]));

        await Assert.ThrowsAsync<EndOfStreamException>(() => pipe.ReadAsync().AsTask());
    }

    [Fact]
    public void ValidateHandshake_RejectsWrongVersionOrToken()
    {
        NetworkHelperMessage wrongVersion = NetworkHelperMessage.Create("handshake", null, new { Token = "secret" }) with { Version = 2 };
        NetworkHelperMessage wrongToken = NetworkHelperMessage.Create("handshake", null, new { Token = "other" });

        Assert.False(NetworkHelperProtocol.ValidateHandshake(wrongVersion, "secret", out _));
        Assert.False(NetworkHelperProtocol.ValidateHandshake(wrongToken, "secret", out _));
    }

    [Fact]
    public void ValidateHandshake_AcceptsMatchingVersionAndToken()
    {
        NetworkHelperMessage message = NetworkHelperMessage.Create("handshake", null, new { Token = "secret" });

        Assert.True(NetworkHelperProtocol.ValidateHandshake(message, "secret", out string? error));
        Assert.Null(error);
    }

    private static async Task<byte[]> SerializeAsync(NetworkHelperMessage message)
    {
        var stream = new MemoryStream();
        await using (var pipe = new LengthPrefixedJsonPipe(stream, leaveOpen: true))
        {
            await pipe.WriteAsync(message);
        }

        return stream.ToArray();
    }

    private sealed class FragmentedReadStream(byte[] data, int maximumReadSize) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, maximumReadSize));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer[..Math.Min(buffer.Length, maximumReadSize)], cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await _inner.DisposeAsync(); await base.DisposeAsync(); }
    }
}
