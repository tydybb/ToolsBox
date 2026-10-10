using System.Buffers.Binary;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ToolsBox.App.WorkCountdown;

internal static class OffWorkArtworkCatalog
{
    private const int MaximumSide = 4096;
    private const long MaximumPixels = 16_000_000;
    private static readonly string[] Keys = ["ride", "rest", "drool", "run", "beagle"];
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, BitmapSource?> Images = new(StringComparer.Ordinal);

    internal static string Select(DateOnly date)
    {
        int block = date.DayNumber / Keys.Length;
        int[] order = Shuffle(block);
        // The previous block's last item never changes when its first two items swap.
        if (block > 0 && order[0] == Shuffle(block - 1)[^1])
            (order[0], order[1]) = (order[1], order[0]);
        return Keys[order[date.DayNumber % Keys.Length]];
    }

    private static int[] Shuffle(int block)
    {
        int[] order = [0, 1, 2, 3, 4];
        var random = new Random(block);
        for (int index = order.Length - 1; index > 0; index--)
        {
            int other = random.Next(index + 1);
            (order[index], order[other]) = (order[other], order[index]);
        }
        return order;
    }

    internal static BitmapSource? GetImage(string? key)
    {
        if (key is not ("ride" or "rest" or "drool" or "run" or "beagle")) return null;
        lock (CacheLock)
        {
            if (!Images.TryGetValue(key, out BitmapSource? image))
            {
                image = LoadResource(key);
                Images.Add(key, image);
            }
            return image;
        }
    }

    private static BitmapSource? LoadResource(string key)
    {
        try
        {
            string assembly = typeof(OffWorkArtworkCatalog).Assembly.GetName().Name!;
            var uri = new Uri($"/{assembly};component/Assets/Countdown/offwork-{key}.png", UriKind.Relative);
            var resource = Application.GetResourceStream(uri);
            return resource is null ? null : DecodeImage(resource.Stream);
        }
        catch (Exception exception) when (IsResourceFailure(exception)) { return null; }
    }

    internal static BitmapSource? DecodeImage(Stream stream)
    {
        using (stream)
        {
            try
            {
                if (!TryReadDimensions(stream, out int width, out int height)) return null;
                BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count != 1) return null;
                BitmapFrame image = decoder.Frames[0];
                if (image.PixelWidth != width || image.PixelHeight != height) return null;
                image.Freeze();
                return image;
            }
            catch (Exception exception) when (IsResourceFailure(exception)) { return null; }
        }
    }

    private static bool TryReadDimensions(Stream stream, out int width, out int height)
    {
        width = height = 0;
        if (!stream.CanRead || !stream.CanSeek) return false;
        long start = stream.Position;
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        stream.Position = start;
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!header[..8].SequenceEqual(signature)
            || BinaryPrimitives.ReadUInt32BigEndian(header[8..12]) != 13
            || !header[12..16].SequenceEqual("IHDR"u8)) return false;
        uint pngWidth = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        uint pngHeight = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        if (pngWidth == 0 || pngHeight == 0 || pngWidth > MaximumSide || pngHeight > MaximumSide
            || (long)pngWidth * pngHeight > MaximumPixels) return false;
        width = (int)pngWidth;
        height = (int)pngHeight;
        return true;
    }

    private static bool IsResourceFailure(Exception exception) => exception is
        IOException or FormatException or NotSupportedException or ArgumentException or InvalidOperationException;
}
