using System.Collections;
using System.Reflection;
using System.Resources;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class OriginalCountdownArtworkTests
{
    [Fact]
    public void PublicAssembly_DoesNotEmbedUnlicensedCountdownPngs()
    {
        Assembly assembly = typeof(App).Assembly;
        foreach (string name in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".g.resources", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var resources = new ResourceReader(stream);
            foreach (DictionaryEntry entry in resources)
            {
                string path = Assert.IsType<string>(entry.Key).Replace('\\', '/');
                Assert.False(path.StartsWith("assets/countdown/", StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase), $"Unlicensed artwork embedded: {path}");
            }
        }
    }

    [Fact]
    public Task PublicCatalog_ProvidesFrozenTransparentAnimationsWithoutPngResources() => WpfTestThread.RunAsync(() =>
    {
        MethodInfo getFrames = Catalog("CountdownSpriteCatalog").GetMethod("GetFrames", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (string character in new[] { "lipu", "duodong", "xiuzhen", "tianzhong", "xiaohe" })
        foreach (string movement in new[] { "", "walk-" })
        foreach (string mood in new[] { "calm", "tired", "happy", "stressed" })
        {
            string key = $"{character}/{movement}{mood}";
            var frames = Assert.IsAssignableFrom<IReadOnlyList<BitmapSource>>(getFrames.Invoke(null, [key]));
            Assert.Equal(6, frames.Count);
            Assert.All(frames, frame => AssertArtwork(frame, 256));
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PublicCatalog_ProvidesFrozenReminderArtworkWithoutPngResources() => WpfTestThread.RunAsync(() =>
    {
        MethodInfo getImage = Catalog("OffWorkArtworkCatalog").GetMethod("GetImage", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (string key in new[] { "ride", "rest", "drool", "run", "beagle" })
            AssertArtwork(Assert.IsAssignableFrom<BitmapSource>(getImage.Invoke(null, [key])), 1024);
        return Task.CompletedTask;
    });

    private static Type Catalog(string name) => typeof(App).Assembly.GetType("ToolsBox.App.WorkCountdown." + name)!;

    private static void AssertArtwork(BitmapSource image, int expectedSide)
    {
        Assert.True(image.IsFrozen);
        Assert.Equal(expectedSide, image.PixelWidth);
        Assert.Equal(expectedSide, image.PixelHeight);
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[expectedSide * expectedSide * 4];
        converted.CopyPixels(pixels, expectedSide * 4, 0);
        Assert.Equal(0, pixels[3]);
        Assert.Contains(Enumerable.Range(0, pixels.Length / 4), i => pixels[i * 4 + 3] >= 250);
    }
}
