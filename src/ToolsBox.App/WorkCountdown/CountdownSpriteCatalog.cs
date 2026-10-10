using System.Buffers.Binary;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ToolsBox.App.WorkCountdown;

internal static class CountdownSpriteCatalog
{
    private const int Columns = 6;
    private const int Rows = 4;
    private const int MaximumSide = 4096;
    private const long MaximumPixels = 16_000_000;
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, IReadOnlyList<BitmapSource>[]> Atlases = new(StringComparer.Ordinal);

    internal static IReadOnlyList<BitmapSource> GetFrames(string? animationKey)
    {
        if (string.IsNullOrEmpty(animationKey)) return [];
        int separator = animationKey.IndexOf('/');
        if (separator <= 0 || separator != animationKey.LastIndexOf('/')) return [];
        string character = animationKey[..separator];
        string mood = animationKey[(separator + 1)..];
        bool walking = mood.StartsWith("walk-", StringComparison.Ordinal);
        int row = MoodRow(walking ? mood[5..] : mood);
        if (row < 0 || character is not ("lipu" or "duodong" or "xiuzhen" or "tianzhong" or "xiaohe")) return [];
        string atlasKey = character + (walking ? "-walk" : "");

        lock (CacheLock)
        {
            if (!Atlases.TryGetValue(atlasKey, out IReadOnlyList<BitmapSource>[]? atlas))
            {
                atlas = LoadResource(atlasKey);
                Atlases.Add(atlasKey, atlas);
            }
            return atlas.Length == Rows ? atlas[row] : [];
        }
    }

    private static IReadOnlyList<BitmapSource>[] LoadResource(string character)
    {
        try
        {
            string assembly = typeof(CountdownSpriteCatalog).Assembly.GetName().Name!;
            var uri = new Uri($"/{assembly};component/Assets/Countdown/{character}.png", UriKind.Relative);
            var resource = Application.GetResourceStream(uri);
            return resource is null ? [] : DecodeAlignedAtlas(resource.Stream);
        }
        catch (Exception exception) when (IsResourceFailure(exception))
        {
            return [];
        }
    }

    internal static IReadOnlyList<BitmapSource>[] DecodeAtlas(Stream stream) => Decode(stream, false);

    internal static IReadOnlyList<BitmapSource>[] DecodeAlignedAtlas(Stream stream) => Decode(stream, true);

    private static IReadOnlyList<BitmapSource>[] Decode(Stream stream, bool align)
    {
        using (stream)
        {
            try
            {
                if (!TryReadDimensions(stream, out int width, out int height)) return [];
                // Validate the PNG header before OnLoad allocates the complete pixel buffer.
                BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count != 1) return [];
                BitmapFrame source = decoder.Frames[0];
                if (source.PixelWidth != width || source.PixelHeight != height) return [];
                source.Freeze();

                if (align) return CountdownAtlasSlicer.Align(source, Columns, Rows);

                int frameWidth = width / Columns, frameHeight = height / Rows;
                var atlas = new IReadOnlyList<BitmapSource>[Rows];
                for (int row = 0; row < Rows; row++)
                {
                    var frames = new BitmapSource[Columns];
                    for (int column = 0; column < Columns; column++)
                    {
                        var frame = new CroppedBitmap(source,
                            new Int32Rect(column * frameWidth, row * frameHeight, frameWidth, frameHeight));
                        frame.Freeze();
                        frames[column] = frame;
                    }
                    atlas[row] = Array.AsReadOnly(frames);
                }
                return atlas;
            }
            catch (Exception exception) when (IsResourceFailure(exception))
            {
                return [];
            }
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
            || (long)pngWidth * pngHeight > MaximumPixels
            || pngWidth % Columns != 0 || pngHeight % Rows != 0) return false;
        width = (int)pngWidth;
        height = (int)pngHeight;
        return true;
    }

    private static int MoodRow(string mood) => mood switch
    {
        "calm" => 0,
        "tired" => 1,
        "happy" => 2,
        "stressed" => 3,
        _ => -1
    };

    private static bool IsResourceFailure(Exception exception) => exception is
        IOException or FormatException or NotSupportedException or ArgumentException or InvalidOperationException;
}
