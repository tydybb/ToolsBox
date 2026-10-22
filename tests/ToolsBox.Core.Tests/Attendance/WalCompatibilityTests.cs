using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ToolsBox.Core.Attendance;

namespace ToolsBox.Core.Tests.Attendance;

public sealed class WalCompatibilityTests
{
    private const int FrameSize = 24 + DingTalkDbDecryptor.PageSize;
    private const uint MaximumPages = DingTalkDbDecryptor.MaximumDatabaseBytes / DingTalkDbDecryptor.PageSize;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneMiBAllocatedWalAppliesCommittedCurrentSaltPrefix(bool littleEndianChecksum)
    {
        using var fixture = new WalFixture();
        var frames = new List<Frame>();
        for (int i = 0; i < 169; i++)
            frames.Add(new Frame(1, i == 168 ? 1U : 0U, i == 168 ? (byte)42 : (byte)30));
        for (int i = 0; i < 85; i++)
            frames.Add(new Frame(1, 1, 99, OldSalt: true));
        byte[] wal = BuildWal(littleEndianChecksum, frames.ToArray());
        int completeFramesEnd = wal.Length;
        Array.Resize(ref wal, 1024 * 1024);
        byte[] oldFrame = BuildWal(littleEndianChecksum, new Frame(1, 1, 99, OldSalt: true));
        oldFrame.AsSpan(32, wal.Length - completeFramesEnd).CopyTo(wal.AsSpan(completeFramesEnd));

        byte[] plain = fixture.Decrypt(wal);

        Assert.Equal(DingTalkDbDecryptor.PageSize, plain.Length);
        Assert.Equal(42, plain[100]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OldSaltEndsCurrentCycleWithoutResumingAtLaterMatchingSalt(bool littleEndianChecksum)
    {
        using var fixture = new WalFixture();
        byte[] wal = BuildWal(littleEndianChecksum,
            new Frame(1, 1, 42),
            new Frame(1, 1, 77, OldSalt: true),
            new Frame(1, 1, 99));

        Assert.Equal(42, fixture.Decrypt(wal)[100]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnusedZeroFrameHeaderEndsCurrentCycleWithoutResuming(bool littleEndianChecksum)
    {
        using var fixture = new WalFixture();
        byte[] wal = BuildWal(littleEndianChecksum,
            new Frame(1, 1, 42),
            new Frame(1, 1, 77),
            new Frame(1, 1, 99));
        wal.AsSpan(32 + FrameSize, 24).Clear();

        Assert.Equal(42, fixture.Decrypt(wal)[100]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TornPhysicalTailDoesNotApplyIncompleteFrame(bool littleEndianChecksum)
    {
        using var fixture = new WalFixture();
        byte[] wal = BuildWal(littleEndianChecksum,
            new Frame(1, 1, 42),
            new Frame(1, 1, 99));
        Array.Resize(ref wal, 32 + FrameSize + 2064);

        Assert.Equal(42, fixture.Decrypt(wal)[100]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PaddingDoesNotApplyValidatedFramesAfterLastCommit(bool littleEndianChecksum)
    {
        using var fixture = new WalFixture();
        byte[] wal = BuildWal(littleEndianChecksum,
            new Frame(1, 1, 42),
            new Frame(1, 0, 99));
        Array.Resize(ref wal, wal.Length + 13);

        Assert.Equal(42, fixture.Decrypt(wal)[100]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OldCycleCommitDoesNotCommitCurrentUncommittedFrames(bool littleEndianChecksum)
    {
        using var fixture = new WalFixture();
        byte[] wal = BuildWal(littleEndianChecksum,
            new Frame(1, 0, 42),
            new Frame(1, 1, 99, OldSalt: true));

        Assert.Equal(WalFixture.MainMarker, fixture.Decrypt(wal)[100]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentCycleCorruptionAfterCommitIsRejectedEvenWhenUncommitted(bool littleEndianChecksum)
    {
        using var fixture = new WalFixture();
        byte[] wal = BuildWal(littleEndianChecksum,
            new Frame(1, 1, 42),
            new Frame(1, 0, 99));
        wal[32 + FrameSize + 24 + 100] ^= 1;

        var error = Assert.Throws<InvalidDataException>(() => fixture.Decrypt(wal));

        Assert.Equal("WAL 帧校验失败。", error.Message);
    }

    [Theory]
    [InlineData(0U, 0U)]
    [InlineData(MaximumPages + 1, 0U)]
    [InlineData(1U, MaximumPages + 1)]
    public void InvalidFullCurrentCycleFrameBoundsAreRejected(uint page, uint databaseSize)
    {
        using var fixture = new WalFixture();
        byte[] wal = BuildWal(true,
            new Frame(1, 1, 42),
            new Frame(page, databaseSize, 99));

        var error = Assert.Throws<InvalidDataException>(() => fixture.Decrypt(wal));

        Assert.Equal("WAL 帧头无效。", error.Message);
    }

    [Theory]
    [InlineData(0, 0U)]
    [InlineData(4, 3007001U)]
    [InlineData(8, 8192U)]
    public void UnsupportedWalHeaderIsRejected(int offset, uint value)
    {
        using var fixture = new WalFixture();
        byte[] wal = BuildWal(true, new Frame(1, 1, 42));
        Write(wal, offset, value);

        var error = Assert.Throws<InvalidDataException>(() => fixture.Decrypt(wal));

        Assert.Equal("WAL 版本不受支持。", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidWalHeaderChecksumIsRejected(bool littleEndianChecksum)
    {
        using var fixture = new WalFixture();
        byte[] wal = BuildWal(littleEndianChecksum, new Frame(1, 1, 42));
        wal[24] ^= 1;

        var error = Assert.Throws<InvalidDataException>(() => fixture.Decrypt(wal));

        Assert.Equal("WAL 头校验失败。", error.Message);
    }

    [Fact]
    public void ValidHeaderOnlyRetainsStableMain()
    {
        using var fixture = new WalFixture();

        Assert.Equal(WalFixture.MainMarker, fixture.Decrypt(BuildWal(true))[100]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OldSaltAtFirstPhysicalFrameRetainsStableMain(bool littleEndianChecksum)
    {
        using var fixture = new WalFixture();
        byte[] wal = BuildWal(littleEndianChecksum, new Frame(1, 1, 99, OldSalt: true));

        Assert.Equal(WalFixture.MainMarker, fixture.Decrypt(wal)[100]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnusedZeroFrameHeaderAtFirstPhysicalFrameRetainsStableMain(bool littleEndianChecksum)
    {
        using var fixture = new WalFixture();
        byte[] wal = BuildWal(littleEndianChecksum, new Frame(1, 1, 99));
        wal.AsSpan(32, 24).Clear();

        Assert.Equal(WalFixture.MainMarker, fixture.Decrypt(wal)[100]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TornFirstPhysicalFrameRetainsStableMain(bool littleEndianChecksum)
    {
        using var fixture = new WalFixture();
        byte[] wal = BuildWal(littleEndianChecksum, new Frame(1, 1, 99));
        Array.Resize(ref wal, 32 + 2064);

        Assert.Equal(WalFixture.MainMarker, fixture.Decrypt(wal)[100]);
    }

    [Fact]
    public void InvalidFullFirstCurrentCycleFrameIsRejected()
    {
        using var fixture = new WalFixture();
        byte[] wal = BuildWal(true, new Frame(0, 0, 99));

        Assert.Throws<InvalidDataException>(() => fixture.Decrypt(wal));
    }

    [Fact]
    public void CorruptedFullFirstCurrentCycleFrameIsRejected()
    {
        using var fixture = new WalFixture();
        byte[] wal = BuildWal(true, new Frame(1, 1, 99));
        wal[56 + 100] ^= 1;

        Assert.Throws<InvalidDataException>(() => fixture.Decrypt(wal));
    }

    private sealed record Frame(uint Page, uint DatabaseSize, byte Marker, bool OldSalt = false);

    private static byte[] BuildWal(bool littleEndianChecksum, params Frame[] frames)
    {
        byte[] wal = new byte[32 + frames.Length * FrameSize];
        Write(wal, 0, littleEndianChecksum ? 0x377f0682U : 0x377f0683U);
        Write(wal, 4, 3007000);
        Write(wal, 8, DingTalkDbDecryptor.PageSize);
        Write(wal, 16, 123);
        Write(wal, 20, 456);
        var sum = Checksum(wal.AsSpan(0, 24), 0, 0, littleEndianChecksum);
        Write(wal, 24, sum.Item1);
        Write(wal, 28, sum.Item2);
        foreach (var (frame, index) in frames.Select((frame, index) => (frame, index)))
        {
            int offset = 32 + index * FrameSize;
            Write(wal, offset, frame.Page);
            Write(wal, offset + 4, frame.DatabaseSize);
            Write(wal, offset + 8, frame.OldSalt ? 789U : 123U);
            Write(wal, offset + 12, frame.OldSalt ? 987U : 456U);
            byte[] encrypted = EncryptPage(frame.Marker);
            encrypted.CopyTo(wal, offset + 24);
            sum = Checksum(wal.AsSpan(offset, 8), sum.Item1, sum.Item2, littleEndianChecksum);
            sum = Checksum(encrypted, sum.Item1, sum.Item2, littleEndianChecksum);
            Write(wal, offset + 16, sum.Item1);
            Write(wal, offset + 20, sum.Item2);
        }
        return wal;
    }

    private static void Write(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);

    private static (uint, uint) Checksum(ReadOnlySpan<byte> data, uint a, uint b, bool littleEndian)
    {
        unchecked
        {
            for (int i = 0; i < data.Length; i += 8)
            {
                a += (littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(data[i..]) : BinaryPrimitives.ReadUInt32BigEndian(data[i..])) + b;
                b += (littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(data[(i + 4)..]) : BinaryPrimitives.ReadUInt32BigEndian(data[(i + 4)..])) + a;
            }
        }
        return (a, b);
    }

    private static byte[] EncryptPage(byte marker)
    {
        byte[] page = new byte[DingTalkDbDecryptor.PageSize];
        Encoding.ASCII.GetBytes("SQLite format 3\0").CopyTo(page, 0);
        page[100] = marker;
        using var aes = Aes.Create();
        aes.Key = new byte[16];
        return aes.EncryptEcb(page, PaddingMode.None);
    }

    private sealed class WalFixture : IDisposable
    {
        internal const byte MainMarker = 7;
        private readonly string directory = Path.Combine(Path.GetTempPath(), "ToolsBox-WalCompatibility-" + Guid.NewGuid().ToString("N"));
        private readonly string databasePath;

        internal WalFixture()
        {
            Directory.CreateDirectory(directory);
            databasePath = Path.Combine(directory, "synthetic.db");
            File.WriteAllBytes(databasePath, EncryptPage(MainMarker));
        }

        internal byte[] Decrypt(byte[] wal)
        {
            File.WriteAllBytes(databasePath + "-wal", wal);
            return DingTalkDbDecryptor.DecryptToMemory(new byte[16], databasePath, null);
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
