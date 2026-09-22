using System.Buffers.Binary;
namespace ToolsBox.MediaDownloads;

public sealed partial class MediaDownloadService
{
    // Structural validation rejects truncated containers; it is not a full pixel decoder.
    private static bool IsJpeg(byte[] bytes)
    {
        var data = bytes.AsSpan();
        if (data.Length < 4 || data[0] != 0xff || data[1] != 0xd8) return false;
        var offset = 2;
        var frame = false;
        var scan = false;
        var entropy = false;
        while (offset < data.Length)
        {
            if (data[offset++] != 0xff) return false;
            while (offset < data.Length && data[offset] == 0xff) offset++;
            if (offset >= data.Length) return false;
            var marker = data[offset++];
            if (marker == 0xd9) return frame && scan && entropy && offset == data.Length;
            if (marker is 0x00 or 0xd8 || marker is >= 0xd0 and <= 0xd7) return false;
            if (offset + 2 > data.Length) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            if (length < 2 || length > data.Length - offset) return false;
            if (marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc))
            {
                if (length < 11 || data[offset + 2] == 0 || BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 3, 2)) == 0 || BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 5, 2)) == 0) return false;
                var components = data[offset + 7];
                if (components == 0 || length != 8 + 3 * components) return false;
                frame = true;
            }
            if (marker == 0xda)
            {
                if (!frame || length < 8 || data[offset + 2] == 0 || length != 6 + 2 * data[offset + 2]) return false;
                scan = true;
                offset += length;
                while (offset < data.Length)
                {
                    if (data[offset] != 0xff) { entropy = true; offset++; continue; }
                    if (offset + 1 >= data.Length) return false;
                    var next = data[offset + 1];
                    if (next == 0 || next is >= 0xd0 and <= 0xd7) { entropy = true; offset += 2; continue; }
                    break;
                }
            }
            else offset += length;
        }
        return false;
    }

    private static bool IsWebP(byte[] bytes)
    {
        var data = bytes.AsSpan();
        if (data.Length < 26 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WEBP"u8) || BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4)) != data.Length - 8) return false;
        var offset = 12;
        var image = false;
        while (offset <= data.Length - 8)
        {
            var kind = data.Slice(offset, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            if (size > data.Length - offset - 8) return false;
            var payload = data.Slice(offset + 8, (int)size);
            if (kind.SequenceEqual("VP8 "u8))
            {
                if (size < 11 || (payload[0] & 1) != 0 || !payload.Slice(3, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }) || (BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2)) & 0x3fff) == 0 || (BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2)) & 0x3fff) == 0) return false;
                image = true;
            }
            else if (kind.SequenceEqual("VP8L"u8))
            {
                if (size < 6 || payload[0] != 0x2f || (payload[4] & 0xe0) != 0) return false;
                image = true;
            }
            else if (kind.SequenceEqual("VP8X"u8))
            {
                if (size != 10 || payload[1] != 0 || payload[2] != 0 || payload[3] != 0) return false;
            }
            offset += checked(8 + (int)size + (int)(size & 1));
        }
        return image && offset == data.Length;
    }
}
