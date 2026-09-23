using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;

namespace ToolsBox.Core.Attendance;

public sealed record DecryptOutcome(int PageCount, int WalFramesApplied, bool WalSkipped);

/// <summary>Version-limited local format reader. Only complete, checksum-validated encrypted WAL is merged.</summary>
public static class DingTalkDbDecryptor
{
    public const int PageSize = 4096;
    public const int MaximumDatabaseBytes = 64 * 1024 * 1024;

    public static byte[] DecryptToMemory(byte[] key, string dbPath, string? walPath)
    {
        string effectiveWal = walPath ?? dbPath + "-wal";
        byte[] encrypted = ReadBounded(dbPath);
        byte[] wal = ReadWal(effectiveWal);
        // A live writer may move pages between main and WAL. Require identical complete paired reads.
        if (!encrypted.AsSpan().SequenceEqual(ReadBounded(dbPath)) || !wal.AsSpan().SequenceEqual(ReadWal(effectiveWal)))
            throw new InvalidDataException("数据库正在变化，请稍后重试。");
        if (encrypted.Length == 0 || encrypted.Length % PageSize != 0)
            throw new InvalidDataException("数据库大小超限或页不完整。");
        byte[] plain = DecryptPages(key, encrypted);
        try { if (wal.Length > 0) plain = MergeWal(key, plain, wal); }
        catch { CryptographicOperations.ZeroMemory(plain); throw; }
        if (!plain.AsSpan(0, 16).SequenceEqual(Encoding.ASCII.GetBytes("SQLite format 3\0")))
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new InvalidDataException("不支持的数据库格式。");
        }
        return plain;
    }

    private static byte[] ReadWal(string path) => File.Exists(path) ? ReadBounded(path) : Array.Empty<byte>();

    private static byte[] ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long length = stream.Length;
        if (length > MaximumDatabaseBytes) throw new InvalidDataException("数据库大小超限。");
        byte[] bytes = new byte[(int)length]; stream.ReadExactly(bytes);
        if (stream.Length != length) throw new InvalidDataException("数据库正在变化。");
        return bytes;
    }

    private static byte[] MergeWal(byte[] key, byte[] main, byte[] wal)
    {
        const int frameSize = 24 + PageSize;
        if (wal.Length < 32 || (wal.Length - 32) % frameSize != 0) throw new InvalidDataException("WAL 帧不完整。");
        uint magic = Read(wal, 0);
        if (magic is not (0x377f0682 or 0x377f0683) || Read(wal, 4) != 3007000 || Read(wal, 8) != PageSize)
            throw new InvalidDataException("WAL 版本不受支持。");
        bool little = magic == 0x377f0682;
        var sum = Checksum(wal.AsSpan(0, 24), 0, 0, little);
        if (sum.Item1 != Read(wal, 24) || sum.Item2 != Read(wal, 28)) throw new InvalidDataException("WAL 头校验失败。");
        int frames = (wal.Length - 32) / frameSize, lastCommit = -1, finalPages = main.Length / PageSize;
        for (int i = 0; i < frames; i++)
        {
            int offset = 32 + i * frameSize;
            uint page = Read(wal, offset), size = Read(wal, offset + 4);
            if (page == 0 || page > MaximumDatabaseBytes / PageSize || size > MaximumDatabaseBytes / PageSize ||
                !wal.AsSpan(offset + 8, 8).SequenceEqual(wal.AsSpan(16, 8))) throw new InvalidDataException("WAL 帧头无效。");
            sum = Checksum(wal.AsSpan(offset, 8), sum.Item1, sum.Item2, little);
            sum = Checksum(wal.AsSpan(offset + 24, PageSize), sum.Item1, sum.Item2, little);
            if (sum.Item1 != Read(wal, offset + 16) || sum.Item2 != Read(wal, offset + 20)) throw new InvalidDataException("WAL 帧校验失败。");
            if (size > 0) { lastCommit = i; finalPages = (int)size; }
        }
        if (lastCommit < 0) return main;
        byte[] merged = new byte[finalPages * PageSize];
        main.AsSpan(0, Math.Min(main.Length, merged.Length)).CopyTo(merged);
        var available = new bool[finalPages];
        Array.Fill(available, true, 0, Math.Min(main.Length / PageSize, finalPages));
        try
        {
            for (int i = 0; i <= lastCommit; i++)
            {
                int offset = 32 + i * frameSize, page = (int)Read(wal, offset);
                if (page > finalPages) continue;
                byte[] decrypted = DecryptPages(key, wal.AsSpan(offset + 24, PageSize));
                try { decrypted.CopyTo(merged, (page - 1) * PageSize); available[page - 1] = true; }
                finally { CryptographicOperations.ZeroMemory(decrypted); }
            }
            if (available.Any(value => !value)) throw new InvalidDataException("WAL 缺少数据库页。");
            CryptographicOperations.ZeroMemory(main);
            return merged;
        }
        catch { CryptographicOperations.ZeroMemory(merged); throw; }
    }

    private static uint Read(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
    private static (uint, uint) Checksum(ReadOnlySpan<byte> data, uint a, uint b, bool little)
    {
        unchecked
        {
            for (int i = 0; i < data.Length; i += 8)
            {
                a += (little ? BinaryPrimitives.ReadUInt32LittleEndian(data[i..]) : BinaryPrimitives.ReadUInt32BigEndian(data[i..])) + b;
                b += (little ? BinaryPrimitives.ReadUInt32LittleEndian(data[(i + 4)..]) : BinaryPrimitives.ReadUInt32BigEndian(data[(i + 4)..])) + a;
            }
        }
        return (a, b);
    }

    /// <summary>Explicit export utility; never overwrites. The checker itself uses memory only.</summary>
    public static DecryptOutcome DecryptToFile(byte[] key, string dbPath, string? walPath, string destination)
    {
        byte[] plain = DecryptToMemory(key, dbPath, walPath);
        try
        {
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(plain);
            return new DecryptOutcome(plain.Length / PageSize, 0, false);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public static byte[] DecryptPages(byte[] key, ReadOnlySpan<byte> data)
    {
        if (data.Length == 0 || data.Length > MaximumDatabaseBytes || data.Length % PageSize != 0)
            throw new ArgumentException("密文长度无效。", nameof(data));
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptEcb(data, PaddingMode.None);
    }
}
