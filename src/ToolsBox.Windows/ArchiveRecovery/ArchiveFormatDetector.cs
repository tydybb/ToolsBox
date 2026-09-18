using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace ToolsBox.Windows.ArchiveRecovery;

internal enum SupportedArchiveFormat { Zip, SevenZip, Rar4, Rar5 }

public static class ArchiveFormatDetector
{
    /// <summary>Checks container structure without loading the archive engine or attempting a password.</summary>
    public static string? Detect(string path)
    {
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Detect(input, path) switch
            {
                SupportedArchiveFormat.Zip => "ZIP",
                SupportedArchiveFormat.SevenZip => "7z",
                _ => "RAR"
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { return null; }
    }

    internal static SupportedArchiveFormat Detect(FileStream stream, string path)
    {
        if (Regex.IsMatch(Path.GetFileName(path), @"(?:\.\d{3}|\.[rz]\d{2}|\.part\d+\.rar)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new NotSupportedException();
        Span<byte> header = stackalloc byte[64];
        int count = stream.Read(header);
        stream.Position = 0;
        ReadOnlySpan<byte> data = header[..count];
        if (data.StartsWith(new byte[] { 0x50, 0x4b, 3, 4 }) || data.StartsWith(new byte[] { 0x50, 0x4b, 5, 6 }))
        {
            if (stream.Length < 22) throw new InvalidDataException();
            // Multi-disk ZIPs are rejected before the native callback can request another file.
            int size = (int)Math.Min(stream.Length, 65557);
            byte[] tail = new byte[size];
            stream.Position = stream.Length - size;
            stream.ReadExactly(tail);
            stream.Position = 0;
            for (int i = tail.Length - 22; i >= 0; i--)
                if (tail.AsSpan(i, 4).SequenceEqual(new byte[] { 0x50, 0x4b, 5, 6 }))
                {
                    // A signature inside the ZIP comment is not an EOCD record unless its
                    // declared comment reaches the actual end of the archive.
                    if (i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20)) != tail.Length)
                        continue;
                    if (BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 4)) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 6)) != 0)
                        throw new NotSupportedException();
                    return SupportedArchiveFormat.Zip;
                }
            throw new InvalidDataException();
        }
        if (data.StartsWith(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c }))
        {
            if (count < 32) throw new InvalidDataException();
            return SupportedArchiveFormat.SevenZip;
        }
        if (data.StartsWith(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 7, 0 }))
        {
            if (count < 14) throw new InvalidDataException();
            if ((BinaryPrimitives.ReadUInt16LittleEndian(data[10..]) & 1) != 0) throw new NotSupportedException();
            return SupportedArchiveFormat.Rar4;
        }
        if (data.StartsWith(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 7, 1, 0 }))
        {
            int offset = 12;
            ReadVInt(data, ref offset); // header size
            ulong type = ReadVInt(data, ref offset);
            ulong flags = ReadVInt(data, ref offset);
            if ((flags & 1) != 0) ReadVInt(data, ref offset); // extra area size
            if ((flags & 2) != 0) ReadVInt(data, ref offset); // data size
            if (type == 1 && (ReadVInt(data, ref offset) & 1) != 0) throw new NotSupportedException();
            return SupportedArchiveFormat.Rar5;
        }
        throw new NotSupportedException();
    }

    private static ulong ReadVInt(ReadOnlySpan<byte> data, ref int offset)
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (offset >= data.Length) throw new InvalidDataException();
            byte b = data[offset++];
            value |= (ulong)(b & 127) << shift;
            if ((b & 128) == 0) return value;
        }
        throw new InvalidDataException();
    }
}
