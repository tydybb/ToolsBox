using System.Collections;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ToolsBox.App.Tests.WorkCountdown;

public sealed class PrivateArtworkBuildTests
{
    private static readonly string[] Characters = ["lipu", "duodong", "xiuzhen", "tianzhong", "xiaohe"];
    private static readonly string[] Moods = ["calm", "tired", "happy", "stressed"];
    private static readonly string[] ReminderKeys = ["ride", "rest", "drool", "run", "beagle"];
    private static bool IsPrivate
    {
        get
        {
            var setting = Assert.Single(typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(a => a.Key == "ToolsBox.PrivateArtwork"));
            Assert.True(bool.TryParse(setting.Value, out bool value));
            return value;
        }
    }

    [Fact]
    public void BuildFlavorHasExplicitMetadataAndExactlyItsIntendedEmbeddedResources()
    {
        bool isPrivate = IsPrivate;
        Assembly assembly = typeof(App).Assembly;
        var names = new List<string>();
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".g.resources", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new ResourceReader(stream);
            foreach (DictionaryEntry entry in reader)
            {
                string path = ((string)entry.Key).Replace('\\', '/').ToLowerInvariant();
                if (path.StartsWith("assets/countdown/", StringComparison.Ordinal)) names.Add(path);
            }
        }
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(isPrivate, version.Contains("-private", StringComparison.Ordinal));
        if (!isPrivate) { Assert.Empty(names); return; }
        string[] expected = Characters.SelectMany(c => new[] { c + ".png", c + "-walk.png" })
            .Concat(ReminderKeys.Select(k => "offwork-" + k + ".png"))
            .Select(n => "assets/countdown/" + n).Order().ToArray();
        Assert.Equal(expected, names.Order().ToArray());
    }

    [Fact]
    public Task PrivateCatalogUsesTheActualOriginalSheetsAndReminderImages() => WpfTestThread.RunAsync(() =>
    {
        // The default/public flavor is checked separately by OriginalCountdownArtworkTests.
        if (!IsPrivate) return Task.CompletedTask;
        Type catalog = typeof(App).Assembly.GetType("ToolsBox.App.WorkCountdown.CountdownSpriteCatalog", true)!;
        MethodInfo get = catalog.GetMethod("GetFrames", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo decode = catalog.GetMethod("DecodeAlignedAtlas", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (string character in Characters)
        foreach (bool walking in new[] { false, true })
        {
            string file = character + (walking ? "-walk" : "") + ".png";
            var expected = Assert.IsAssignableFrom<IReadOnlyList<BitmapSource>[]>(decode.Invoke(null, [Open(file)]));
            Assert.Equal(4, expected.Length);
            for (int row = 0; row < Moods.Length; row++)
            {
                var actual = Assert.IsAssignableFrom<IReadOnlyList<BitmapSource>>(get.Invoke(null,
                    [$"{character}/{(walking ? "walk-" : "")}{Moods[row]}"]));
                Assert.Equal(6, actual.Count);
                for (int column = 0; column < actual.Count; column++)
                {
                    Assert.True(actual[column].IsFrozen);
                    Assert.Equal(PixelHash(expected[row][column]), PixelHash(actual[column]));
                }
            }
        }
        Type reminders = typeof(App).Assembly.GetType("ToolsBox.App.WorkCountdown.OffWorkArtworkCatalog", true)!;
        foreach (string key in ReminderKeys)
        {
            var expected = Assert.IsAssignableFrom<BitmapSource>(reminders.GetMethod("DecodeImage", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [Open("offwork-" + key + ".png")]));
            var actual = Assert.IsAssignableFrom<BitmapSource>(reminders.GetMethod("GetImage", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [key]));
            Assert.True(actual.IsFrozen);
            Assert.Equal(PixelHash(expected), PixelHash(actual));
        }
        return Task.CompletedTask;
    });

    private static System.IO.Stream Open(string file) => Application.GetResourceStream(new Uri(
        $"/{typeof(App).Assembly.GetName().Name};component/Assets/Countdown/{file}", UriKind.Relative))!.Stream;

    private static string PixelHash(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return Convert.ToHexString(SHA256.HashData(pixels));
    }
}
