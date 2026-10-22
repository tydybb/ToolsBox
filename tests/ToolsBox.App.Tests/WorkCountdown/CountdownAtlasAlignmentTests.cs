using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class CountdownAtlasAlignmentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AlignedAtlas_PreservesCompleteCrossCellCharactersAndTheirAccessories(bool backgroundNoise) => WpfTestThread.RunAsync(() =>
    {
        using var stream = MakeUnevenAtlas(backgroundNoise);
        var atlas = DecodeAligned(stream);
        Assert.False(stream.CanRead);
        Assert.Equal(4, atlas.Length);
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 6; column++)
        {
            var frame = atlas[row][column];
            Assert.Equal(256, frame.PixelWidth);
            Assert.Equal(256, frame.PixelHeight);
            Assert.True(frame.IsFrozen);
            byte[] pixels = Pixels(frame);
            Color body = BodyColor(row, column), accessory = AccessoryColor(row, column);
            int bodyPixels = 0, accessoryPixels = 0;
            for (int offset = 0; offset < pixels.Length; offset += 4)
            {
                if (pixels[offset + 3] < 250) continue;
                Color actual = Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
                if (Near(actual, body)) bodyPixels++;
                else if (Near(actual, accessory)) accessoryPixels++;
                // Resampling can blend a frame's own touching body and accessory, never a neighbour's palette.
                else if (!Between(actual, body, accessory))
                    Assert.Fail($"Foreign character pixels in row {row}, column {column}: {actual}");
            }
            Assert.True(bodyPixels > 10_000);
            Assert.True(accessoryPixels > 60, $"Detached bubble/gesture was lost at {row},{column}.");
            AssertClearEdges(frame, $"synthetic/{row}/{column}");
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task AlignedAtlas_RefusesAnImageWithNoSafeTransparentSeparators() => WpfTestThread.RunAsync(() =>
    {
        byte[] pixels = Enumerable.Repeat((byte)255, 1536 * 1024 * 4).ToArray();
        using var stream = Encode(pixels);
        Assert.Empty(DecodeAligned(stream));
        Assert.False(stream.CanRead);
        return Task.CompletedTask;
    });

    [Fact]
    public Task EmbeddedCatalog_All240FramesHaveClearMarginsWithoutCroppedParts() => WpfTestThread.RunAsync(() =>
    {
        Type catalog = typeof(App).Assembly.GetType("ToolsBox.App.WorkCountdown.CountdownSpriteCatalog")!;
        MethodInfo get = catalog.GetMethod("GetFrames", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (string character in new[] { "lipu", "duodong", "xiuzhen", "tianzhong", "xiaohe" })
        foreach (string mode in new[] { "", "walk-" })
        foreach (string mood in new[] { "calm", "tired", "happy", "stressed" })
        {
            string key = $"{character}/{mode}{mood}";
            var frames = Assert.IsAssignableFrom<IReadOnlyList<BitmapSource>>(get.Invoke(null, [key]));
            Assert.Equal(6, frames.Count);
            for (int frame = 0; frame < 6; frame++) AssertClearEdges(frames[frame], $"{key}/{frame}");
        }
        return Task.CompletedTask;
    });

    private static void AssertClearEdges(BitmapSource source, string key)
    {
        byte[] pixels = Pixels(source);
        int width = source.PixelWidth, height = source.PixelHeight;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (x >= 6 && x < width - 6 && y >= 6 && y < height - 6) continue;
            Assert.True(pixels[(y * width + x) * 4 + 3] <= 4,
                $"{key} has clipped/foreign content at edge ({x},{y}).");
        }
    }

    private static IReadOnlyList<BitmapSource>[] DecodeAligned(Stream stream)
    {
        var type = typeof(App).Assembly.GetType("ToolsBox.App.WorkCountdown.CountdownSpriteCatalog");
        Assert.NotNull(type);
        var method = type.GetMethod("DecodeAlignedAtlas", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<IReadOnlyList<BitmapSource>[]>(method.Invoke(null, [stream]));
    }

    private static MemoryStream MakeUnevenAtlas(bool noise)
    {
        byte[] pixels = new byte[1536 * 1024 * 4];
        if (noise)
            for (int offset = 3; offset < pixels.Length; offset += 4) pixels[offset] = 1;
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 6; column++)
        {
            int x = column * 256, y = row * 256;
            Color color = BodyColor(row, column);
            Fill(pixels, x + 80, y + 40, 152, 108, color);
            Fill(pixels, x + 92, y + 100, 144, 138, color);
            Fill(pixels, x + 232, y + 82, column == 5 ? 16 : 32, 30, color);
            Fill(pixels, x + 104, y + 225, 42, row == 3 ? 25 : 38, color);
            Fill(pixels, x + 178, y + 225, 42, row == 3 ? 25 : 38, color);
            // Detached expressive accessory straddles a nominal column border, but belongs to this frame.
            Fill(pixels, x + (column == 5 ? 228 : 242), y + 152, column == 5 ? 16 : 30, 16, AccessoryColor(row, column));
        }
        return Encode(pixels);
    }

    private static void Fill(byte[] pixels, int left, int top, int width, int height, Color color)
    {
        for (int y = top; y < Math.Min(1024, top + height); y++)
        for (int x = left; x < Math.Min(1536, left + width); x++)
        {
            int offset = (y * 1536 + x) * 4;
            pixels[offset] = color.B; pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.R; pixels[offset + 3] = 255;
        }
    }

    private static Color BodyColor(int row, int column) => Color.FromRgb((byte)(40 + row * 35), (byte)(35 + column * 30), 99);
    private static Color AccessoryColor(int row, int column) => Color.FromRgb((byte)(45 + row * 35), (byte)(40 + column * 30), 210);
    private static bool Near(Color actual, Color expected) => Math.Abs(actual.R - expected.R) <= 1
        && Math.Abs(actual.G - expected.G) <= 1 && Math.Abs(actual.B - expected.B) <= 1;
    private static bool Between(Color actual, Color a, Color b) =>
        actual.R >= Math.Min(a.R, b.R) - 1 && actual.R <= Math.Max(a.R, b.R) + 1
        && actual.G >= Math.Min(a.G, b.G) - 1 && actual.G <= Math.Max(a.G, b.G) + 1
        && actual.B >= Math.Min(a.B, b.B) - 1 && actual.B <= Math.Max(a.B, b.B) + 1;

    private static MemoryStream Encode(byte[] pixels)
    {
        var source = BitmapSource.Create(1536, 1024, 96, 96, PixelFormats.Bgra32, null, pixels, 1536 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0; return stream;
    }

    private static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0); return pixels;
    }
}
