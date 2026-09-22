using System.Reflection;
using ToolsBox.Core.WebResources;

namespace ToolsBox.Core.Tests.WebResources;

public class NewWindowNavigationPolicyTests
{
    [Theory]
    [InlineData("https://www.douyin.com/video/7680095187900697907")]
    [InlineData("http://example.test/watch?id=123")]
    [InlineData("https://example.test/watch?signature=a%2Fb%2Bc#player")]
    public void AllowsUserInitiatedWebNavigation(string url)
        => Assert.True(CanNavigate(url, true));

    [Theory]
    [InlineData("https://www.douyin.com/video/7680095187900697907")]
    [InlineData("http://example.test/watch?id=123")]
    public void BlocksAutomaticNewWindows(string url)
        => Assert.False(CanNavigate(url, false));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///C:/private.txt")]
    [InlineData("blob:https://example.test/id")]
    [InlineData("about:blank")]
    [InlineData("mailto:user@example.test")]
    [InlineData("ms-settings:display")]
    [InlineData("//example.test/watch")]
    [InlineData("/watch?id=123")]
    [InlineData("https://user:password@example.test/watch")]
    [InlineData("https://user@example.test/watch")]
    [InlineData("https://example.test/watch\r\nInjected: value")]
    [InlineData("https://example.test/\twatch")]
    [InlineData("https://example.test/watch\0")]
    [InlineData("")]
    [InlineData(null)]
    public void BlocksUnsafeOrInvalidTargetsEvenForUserClicks(string? url)
        => Assert.False(CanNavigate(url, true));

    [Fact]
    public void BlocksTargetsLongerThanTheSharedWebUrlLimit()
        => Assert.False(CanNavigate("https://example.test/?q=" + new string('a', 16384), true));

    private static bool CanNavigate(string? url, bool isUserInitiated)
    {
        var policy = typeof(WebResourceRules).Assembly.GetType("ToolsBox.Core.WebResources.NewWindowNavigationPolicy");
        Assert.NotNull(policy);
        var method = policy.GetMethod("CanNavigate", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method.Invoke(null, [url, isUserInitiated]));
    }
}
