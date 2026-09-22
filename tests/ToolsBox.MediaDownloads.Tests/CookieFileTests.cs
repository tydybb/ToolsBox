using System.Reflection;
using ToolsBox.MediaDownloads;

namespace ToolsBox.MediaDownloads.Tests;
public class CookieFileTests
{
    private static Type CookieType => typeof(MediaCookie).Assembly.GetType("ToolsBox.MediaDownloads.CookieFile") ?? throw new Xunit.Sdk.XunitException("CookieFile missing");
    [Fact]
    public void BrowserCookiesWithEmptyNamesRetainTheirNetscapeFields()
    {
        const string value = "bare_token";
        var cookies = new[] { new MediaCookie("", value, ".example.test", "/", true, null, false) };
        using var file = (IDisposable)CookieType.GetMethod("Create")!.Invoke(null, [cookies])!;
        var path = (string)CookieType.GetProperty("Path")!.GetValue(file)!;
        var fields = File.ReadAllLines(path)[1].Split('\t');
        Assert.Equal(new[] { ".example.test", "TRUE", "/", "TRUE", "0", "", value }, fields);
    }

    [Theory]
    [InlineData("", "", "example.test", "/")]
    [InlineData("bad\tname", "value", "example.test", "/")]
    [InlineData("", "bad\nvalue", "example.test", "/")]
    [InlineData("", "bad\rvalue", "example.test", "/")]
    [InlineData("", "bad\0value", "example.test", "/")]
    [InlineData("", "value", "bad\tdomain", "/")]
    [InlineData("", "value", "example.test", "/bad\npath")]
    [InlineData("", "value", "not a domain", "/")]
    [InlineData("", "value", "example.test", "relative")]
    public void NamelessCookieSupportDoesNotAllowInvalidFields(string name, string value, string domain, string path)
    {
        var cookies = new[] { new MediaCookie(name, value, domain, path, false, null) };
        var error = Assert.Throws<TargetInvocationException>(() => CookieType.GetMethod("Create")!.Invoke(null, [cookies]));
        Assert.IsType<ArgumentException>(error.InnerException);
    }

    [Fact]
    public void ScopedCookiesAreTemporaryAndHostOnly()
    {
        var cookies = new[] { new MediaCookie("session", "secret", "example.test", "/", true, null) };
        var file = (IDisposable)CookieType.GetMethod("Create")!.Invoke(null, [cookies])!;
        var path = (string)CookieType.GetProperty("Path")!.GetValue(file)!;
        var content = File.ReadAllText(path);
        Assert.Contains("example.test\tFALSE\t/\tTRUE\t0\tsession\tsecret", content);
        file.Dispose();
        Assert.False(File.Exists(path));
    }
    [Fact]
    public void RejectsCookieLineInjection()
    {
        var cookies = new[] { new MediaCookie("a", "x\n.evil.test", "example.test", "/", false, null) };
        var error = Assert.Throws<TargetInvocationException>(() => CookieType.GetMethod("Create")!.Invoke(null, [cookies]));
        Assert.IsType<ArgumentException>(error.InnerException);
    }
}
