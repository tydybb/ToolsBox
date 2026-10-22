using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class OffWorkArtworkCatalogTests
{
    private static readonly string[] Keys = ["ride", "rest", "drool", "run", "beagle"];

    [Fact]
    public void Selection_IsStableForEveryOpenOnTheSameActualDate()
    {
        foreach (DateOnly date in new[] { new DateOnly(2026, 10, 8), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 1) })
        {
            string first = Select(date);
            Assert.Contains(first, Keys);
            for (int open = 0; open < 20; open++) Assert.Equal(first, Select(date));
        }
    }

    [Fact]
    public void Selection_VisitsAllFiveArtworksPerBlockAndChangesOnAdjacentDaysAcrossYears()
    {
        int start = new DateOnly(2024, 1, 1).DayNumber;
        int end = new DateOnly(2030, 1, 10).DayNumber;
        for (int day = start; day < end; day++)
        {
            string selected = Select(DateOnly.FromDayNumber(day));
            Assert.Contains(selected, Keys);
            Assert.NotEqual(selected, Select(DateOnly.FromDayNumber(day + 1)));
            if (day % Keys.Length == 0)
                Assert.Equal(Keys.Length, Enumerable.Range(day, Keys.Length)
                    .Select(number => Select(DateOnly.FromDayNumber(number))).Distinct().Count());
        }
        string[] firstYear = Enumerable.Range(0, 20).Select(i => Select(new DateOnly(2026, 10, 1).AddDays(i))).ToArray();
        string[] followingYear = Enumerable.Range(0, 20).Select(i => Select(new DateOnly(2027, 10, 1).AddDays(i))).ToArray();
        Assert.False(firstYear.SequenceEqual(followingYear));
    }

    [Fact]
    public void Selection_SupportsFirstAndLastDateWithoutOverflow()
    {
        Assert.Contains(Select(DateOnly.MinValue), Keys);
        Assert.Contains(Select(DateOnly.MaxValue), Keys);
    }

    [Fact]
    public Task EmbeddedArtworks_AreHighResolutionTransparentFrozenAndCached() => WpfTestThread.RunAsync(() =>
    {
        foreach (string key in Keys)
        {
            BitmapSource image = Assert.IsAssignableFrom<BitmapSource>(GetImage(key));
            Assert.Same(image, GetImage(key));
            Assert.True(image.IsFrozen);
            Assert.InRange(image.PixelWidth, 1024, 4096);
            Assert.InRange(image.PixelHeight, 1024, 4096);
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            byte[] pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
            converted.CopyPixels(pixels, image.PixelWidth * 4, 0);
            Assert.Contains(Enumerable.Range(0, pixels.Length / 4), i => pixels[i * 4 + 3] == 0);
            Assert.Contains(Enumerable.Range(0, pixels.Length / 4), i => pixels[i * 4 + 3] >= 250);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../ride")]
    [InlineData("ride/extra")]
    [InlineData("missing")]
    public Task InvalidKey_DoesNotLoadAnImage(string? key) => WpfTestThread.RunAsync(() =>
    {
        Assert.Null(GetImage(key));
        return Task.CompletedTask;
    });

    [Fact]
    public Task SingleImageDecoder_PreservesAlphaAndDisposesTheStream() => WpfTestThread.RunAsync(() =>
    {
        byte[] pixels = [0, 0, 0, 0, 10, 20, 30, 255, 40, 50, 60, 128, 70, 80, 90, 255];
        var source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        var image = Assert.IsAssignableFrom<BitmapSource>(DecodeImage(stream));
        Assert.False(stream.CanRead);
        Assert.True(image.IsFrozen);
        Assert.Equal(2, image.PixelWidth);
        Assert.Equal(2, image.PixelHeight);
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        byte[] result = new byte[pixels.Length];
        converted.CopyPixels(result, 8, 0);
        Assert.Equal(pixels, result);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(0, 1024)]
    [InlineData(5000, 1024)]
    [InlineData(4096, 4096)]
    public Task SingleImageDecoder_RejectsUnsafeDimensionsBeforePixelAllocation(int width, int height) => WpfTestThread.RunAsync(() =>
    {
        byte[] header = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(header.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20, 4), height);
        var stream = new MemoryStream(header);
        Assert.Null(DecodeImage(stream));
        Assert.False(stream.CanRead);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SingleImageDecoder_CorruptDataReturnsEmptyAndReleasesStream() => WpfTestThread.RunAsync(() =>
    {
        var stream = new MemoryStream([1, 2, 3, 4]);
        Assert.Null(DecodeImage(stream));
        Assert.False(stream.CanRead);
        return Task.CompletedTask;
    });

    private static Type CatalogType()
    {
        Type? type = typeof(App).Assembly.GetType("ToolsBox.App.WorkCountdown.OffWorkArtworkCatalog");
        Assert.NotNull(type);
        return type!;
    }

    private static string Select(DateOnly date) => Assert.IsType<string>(CatalogType()
        .GetMethod("Select", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [date]));

    private static BitmapSource? GetImage(string? key) => (BitmapSource?)CatalogType()
        .GetMethod("GetImage", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [key]);

    private static BitmapSource? DecodeImage(Stream stream) => (BitmapSource?)CatalogType()
        .GetMethod("DecodeImage", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [stream]);
}
