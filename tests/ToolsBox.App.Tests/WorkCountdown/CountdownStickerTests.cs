using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class CountdownStickerTests
{
    private static readonly string[] Characters = ["lipu", "duodong", "xiuzhen", "tianzhong", "xiaohe"];
    private static readonly string[] Moods = ["calm", "tired", "happy", "stressed"];

    [Theory]
    [InlineData("calm", 0)]
    [InlineData("tired", 1)]
    [InlineData("happy", 2)]
    [InlineData("stressed", 3)]
    public Task Catalog_CropsSixOrderedFrozenFramesAndClosesStream(string mood, int row) => WpfTestThread.RunAsync(() =>
    {
        var stream = MakeAtlas();
        IReadOnlyList<BitmapSource> frames = DecodeFrames(stream, mood);

        Assert.False(stream.CanRead);
        Assert.Equal(6, frames.Count);
        for (int column = 0; column < frames.Count; column++)
        {
            Assert.True(frames[column].IsFrozen);
            Assert.Equal(2, frames[column].PixelWidth);
            Assert.Equal(2, frames[column].PixelHeight);
            Assert.Equal(FrameColor(row, column), FirstPixel(frames[column]));
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(4098, 4)]
    [InlineData(4092, 4096)]
    [InlineData(13, 8)]
    [InlineData(12, 9)]
    [InlineData(0, 8)]
    public Task Catalog_RejectsOversizedOrIncompleteGridBeforeDecoding(int width, int height) => WpfTestThread.RunAsync(() =>
    {
        using var atlas = MakeAtlas();
        byte[] bytes = atlas.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        var stream = new CountingStream(bytes);

        Assert.Empty(DecodeFrames(stream, "calm"));
        Assert.False(stream.CanRead);
        Assert.Equal(24, stream.BytesRead);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Catalog_CorruptPngReleasesStreamAndInvalidMoodReturnsEmpty() => WpfTestThread.RunAsync(() =>
    {
        var corrupt = new MemoryStream([1, 2, 3, 4]);
        Assert.Empty(DecodeFrames(corrupt, "calm"));
        Assert.False(corrupt.CanRead);

        using var atlas = MakeAtlas();
        var incomplete = new MemoryStream(atlas.ToArray()[..24]);
        Assert.Empty(DecodeFrames(incomplete, "calm"));
        Assert.False(incomplete.CanRead);

        Assert.Empty(GetFrames("lipu/unknown"));
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lipu")]
    [InlineData("../lipu/calm")]
    [InlineData("missing/calm")]
    [InlineData("lipu/missing")]
    [InlineData("lipu/calm/extra")]
    public Task Sticker_InvalidKeyFallsBackToEmptySource(string? key) => WpfTestThread.RunAsync(() =>
    {
        Image sticker = CreateSticker();
        Set(sticker, "AnimationKey", key);

        Assert.Null(sticker.Source);
        Assert.False(Get<bool>(sticker, "IsPlaying"));
        Assert.Equal(0, Get<int>(sticker, "CurrentFrameIndex"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task EmbeddedCatalog_CachesTwentyAnimationsWithSixDistinctTransparentFrames() => WpfTestThread.RunAsync(() =>
    {
        foreach (string character in Characters)
        foreach (string mood in Moods)
        {
            string key = character + "/" + mood;
            IReadOnlyList<BitmapSource> frames = GetFrames(key);
            Assert.Equal(6, frames.Count);
            Assert.Same(frames, GetFrames(key));
            Assert.All(frames, frame =>
            {
                Assert.True(frame.IsFrozen);
                Assert.InRange(frame.PixelWidth, 1, 682);
                Assert.InRange(frame.PixelHeight, 1, 1024);
                byte[] pixels = BgraPixels(frame);
                Assert.Contains(Enumerable.Range(0, pixels.Length / 4), index => pixels[index * 4 + 3] == 0);
                Assert.Contains(Enumerable.Range(0, pixels.Length / 4), index => pixels[index * 4 + 3] > 0);
            });
            string[] hashes = frames.Select(frame => Convert.ToHexString(SHA256.HashData(BgraPixels(frame)))).ToArray();
            Assert.Equal(6, hashes.Distinct().Count());
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EmbeddedCatalog_WalkingRowsAreTransparentDistinctAndCached() => WpfTestThread.RunAsync(() =>
    {
        foreach (string character in Characters)
        foreach (string mood in Moods)
        {
            string key = character + "/walk-" + mood;
            var frames = GetFrames(key);
            Assert.Equal(6, frames.Count);
            Assert.Same(frames, GetFrames(key));
            Assert.All(frames, frame =>
            {
                Assert.True(frame.IsFrozen);
                Assert.Equal(256, frame.PixelWidth);
                Assert.Equal(256, frame.PixelHeight);
                byte[] pixels = BgraPixels(frame);
                Assert.Equal(0, pixels[3]);
                Assert.Equal(0, pixels[^1]);
                // imagegen exports these sheets with near-opaque alpha 254, not exactly 255.
                Assert.Contains(Enumerable.Range(0, pixels.Length / 4), i => pixels[i * 4 + 3] >= 250);
            });
            Assert.Equal(6, frames.Select(f => Convert.ToHexString(SHA256.HashData(BgraPixels(f)))).Distinct().Count());
        }
        Assert.Empty(GetFrames("lipu/walk-unknown"));
        Assert.Empty(GetFrames("../lipu/walk-calm"));
        Assert.Empty(GetFrames("unknown/walk-calm"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Sticker_LoadedFramesAdvanceAndSameKeyPreservesCurrentFrame() => WpfTestThread.RunAsync(async () =>
    {
        Image sticker = CreateSticker();
        Set(sticker, "AnimationKey", "lipu/calm");
        Assert.True(Get<bool>(sticker, "AnimationEnabled"));
        Assert.False(Get<bool>(sticker, "IsPlaying"));
        ImageSource first = Assert.IsAssignableFrom<ImageSource>(sticker.Source);
        Window window = Host(sticker);
        try
        {
            window.Show();
            await SettleAsync();
            Assert.True(sticker.IsLoaded && sticker.IsVisible);
            Assert.True(Get<bool>(sticker, "IsPlaying"));
            await WaitForAsync(() => Get<int>(sticker, "CurrentFrameIndex") > 0);
            Assert.NotSame(first, sticker.Source);
            await WaitForAsync(() => Get<int>(sticker, "CurrentFrameIndex") == 0);
            Assert.Same(first, sticker.Source);
            await WaitForAsync(() => Get<int>(sticker, "CurrentFrameIndex") > 0);

            int frame = Get<int>(sticker, "CurrentFrameIndex");
            ImageSource? source = sticker.Source;
            Set(sticker, "AnimationKey", "lipu/calm");
            Assert.Equal(frame, Get<int>(sticker, "CurrentFrameIndex"));
            Assert.Same(source, sticker.Source);

            Set(sticker, "AnimationEnabled", false);
            Assert.False(Get<bool>(sticker, "IsPlaying"));
            await AssertFrameStoppedAsync(sticker);
            Set(sticker, "AnimationEnabled", true);
            Assert.True(Get<bool>(sticker, "IsPlaying"));
            await WaitForAsync(() => Get<int>(sticker, "CurrentFrameIndex") != frame);
            Set(sticker, "AnimationKey", "lipu/happy");
            Assert.Equal(0, Get<int>(sticker, "CurrentFrameIndex"));
            Assert.Same(GetFrames("lipu/happy")[0], sticker.Source);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Sticker_HiddenCollapsedAndMinimizedPauseThenResumeFrames() => WpfTestThread.RunAsync(async () =>
    {
        Image sticker = CreateSticker();
        Set(sticker, "AnimationKey", "duodong/tired");
        Window window = Host(sticker);
        try
        {
            window.Show();
            await SettleAsync();
            Assert.True(Get<bool>(sticker, "IsPlaying"));

            window.WindowState = WindowState.Minimized;
            await SettleAsync();
            Assert.False(Get<bool>(sticker, "IsPlaying"));
            await AssertFrameStoppedAsync(sticker);
            window.WindowState = WindowState.Normal;
            await SettleAsync();
            await AssertFramesResumeAsync(sticker);

            sticker.Visibility = Visibility.Collapsed;
            await SettleAsync();
            Assert.False(Get<bool>(sticker, "IsPlaying"));
            await AssertFrameStoppedAsync(sticker);
            sticker.Visibility = Visibility.Visible;
            await SettleAsync();
            await AssertFramesResumeAsync(sticker);

            window.Hide();
            await SettleAsync();
            Assert.False(Get<bool>(sticker, "IsPlaying"));
            await AssertFrameStoppedAsync(sticker);
            window.Show();
            await SettleAsync();
            await AssertFramesResumeAsync(sticker);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task Sticker_UnloadedDetachesOldWindowAndResumesInNewHost() => WpfTestThread.RunAsync(async () =>
    {
        Image sticker = CreateSticker();
        Set(sticker, "AnimationKey", "xiuzhen/stressed");
        Window first = Host(sticker);
        Window second = Host(null);
        try
        {
            first.Show();
            await SettleAsync();
            await AssertFramesResumeAsync(sticker);
            first.Content = null;
            await SettleAsync();
            Assert.False(sticker.IsLoaded);
            Assert.False(Get<bool>(sticker, "IsPlaying"));
            await AssertFrameStoppedAsync(sticker);
            first.WindowState = WindowState.Minimized;
            first.WindowState = WindowState.Normal;
            Assert.False(Get<bool>(sticker, "IsPlaying"));

            second.Content = sticker;
            second.Show();
            await SettleAsync();
            await AssertFramesResumeAsync(sticker);
            first.WindowState = WindowState.Minimized;
            await SettleAsync();
            Assert.True(Get<bool>(sticker, "IsPlaying"));
            Set(sticker, "AnimationKey", "missing/happy");
            Assert.Null(sticker.Source);
            Assert.False(Get<bool>(sticker, "IsPlaying"));
        }
        finally { first.Close(); second.Close(); }
    });

    private static Type CatalogType()
    {
        Type? type = typeof(App).Assembly.GetType("ToolsBox.App.WorkCountdown.CountdownSpriteCatalog");
        Assert.NotNull(type);
        return type!;
    }

    private static IReadOnlyList<BitmapSource> DecodeFrames(Stream stream, string mood)
    {
        var atlas = Assert.IsType<IReadOnlyList<BitmapSource>[]>(CatalogType()
            .GetMethod("DecodeAtlas", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [stream]));
        return atlas.Length == 0 ? [] : atlas[Array.IndexOf(Moods, mood)];
    }

    private static IReadOnlyList<BitmapSource> GetFrames(string key) =>
        Assert.IsAssignableFrom<IReadOnlyList<BitmapSource>>(CatalogType()
            .GetMethod("GetFrames", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [key]));

    private static Image CreateSticker()
    {
        Type? type = typeof(App).Assembly.GetType("ToolsBox.App.WorkCountdown.CountdownSticker");
        Assert.NotNull(type);
        return Assert.IsAssignableFrom<Image>(Activator.CreateInstance(type!));
    }

    private static T Get<T>(Image sticker, string property) =>
        Assert.IsType<T>(sticker.GetType().GetProperty(property)!.GetValue(sticker));

    private static void Set(Image sticker, string property, object? value) =>
        sticker.GetType().GetProperty(property)!.SetValue(sticker, value);

    private static Window Host(Image? sticker) => new()
    {
        Content = sticker, Width = 200, Height = 200, WindowStyle = WindowStyle.None,
        ShowInTaskbar = false, ShowActivated = false, Opacity = 0
    };

    private static async Task SettleAsync() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition());
    }

    private static async Task AssertFrameStoppedAsync(Image sticker)
    {
        int frame = Get<int>(sticker, "CurrentFrameIndex");
        ImageSource? source = sticker.Source;
        await Task.Delay(350);
        Assert.Equal(frame, Get<int>(sticker, "CurrentFrameIndex"));
        Assert.Same(source, sticker.Source);
    }

    private static async Task AssertFramesResumeAsync(Image sticker)
    {
        Assert.True(Get<bool>(sticker, "IsPlaying"));
        int frame = Get<int>(sticker, "CurrentFrameIndex");
        await WaitForAsync(() => Get<int>(sticker, "CurrentFrameIndex") != frame);
    }

    private static MemoryStream MakeAtlas()
    {
        const int width = 12, height = 8;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Color color = FrameColor(y / 2, x / 2);
            int index = (y * width + x) * 4;
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
            pixels[index + 3] = color.A;
        }
        BitmapSource source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return stream;
    }

    private static Color FrameColor(int row, int column) => Color.FromArgb(255, (byte)(20 + row * 30), (byte)(30 + column * 30), 99);

    private static Color FirstPixel(BitmapSource source)
    {
        byte[] pixels = BgraPixels(source);
        return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }

    private static byte[] BgraPixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
    }
}
