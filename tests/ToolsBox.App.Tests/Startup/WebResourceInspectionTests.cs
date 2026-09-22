using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ToolsBox.App.WebResources;
using ToolsBox.Core.WebResources;

namespace ToolsBox.App.Tests.Startup;

public class WebResourceInspectionTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Sensitive = "Cookie=session-secret https://private.example/video?token=secret";

    [Fact]
    public Task UninitializedBrowserCannotParseStaleCachedPage() => WpfTestThread.RunAsync(() =>
    {
        var window = NewWindow();
        try
        {
            typeof(WebResourceWindow).GetField("_page", PrivateInstance)!.SetValue(window, "https://example.test/stale");
            typeof(WebResourceWindow).GetMethod("InspectPage", PrivateInstance)!.Invoke(window, [window, new RoutedEventArgs()]);
            Assert.Empty(((DataGrid)window.FindName("ResourcesGrid")).Items);
            Assert.Contains("浏览器尚未就绪", Status(window));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ParserFailureIdentifiesParsingStage() => WpfTestThread.RunAsync(async () =>
    {
        var window = NewWindow();
        try
        {
            var row = new WebResourceRow("https://example.test/video", "https://example.test", WebResourceKind.Video, null);
            await (Task)typeof(WebResourceWindow).GetMethod("Inspect", PrivateInstance)!.Invoke(window, [row])!;
            Assert.Contains("解析视频", Status(window));
            Assert.Contains("组件", Status(window));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("ReadingCookies", "读取网站登录信息")]
    [InlineData("ReadingBrowserIdentity", "读取浏览器标识")]
    [InlineData("Parsing", "解析视频")]
    [InlineData("UpdatingResult", "更新解析结果")]
    public Task FailureReportsSafeStageWithoutSensitiveExceptionText(string stage, string expected) => WpfTestThread.RunAsync(() =>
    {
        var window = NewWindow();
        try
        {
            ReportFailure(window, new ArgumentException(Sensitive), stage);
            Assert.Contains(expected, Status(window));
            Assert.DoesNotContain("Cookie", Status(window));
            Assert.DoesNotContain("secret", Status(window));
            Assert.DoesNotContain("https://", Status(window));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OpenWindowReportsTimeoutInsteadOfRemainingBusy(bool timeoutException) => WpfTestThread.RunAsync(() =>
    {
        var window = NewWindow();
        try
        {
            ((TextBlock)window.FindName("Status")).Text = "正在解析清晰度…";
            ReportFailure(window, timeoutException ? new TimeoutException(Sensitive) : new OperationCanceledException(Sensitive), "Parsing");
            Assert.Contains("超时", Status(window));
            Assert.DoesNotContain("正在解析", Status(window));
            Assert.DoesNotContain("secret", Status(window));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LifetimeCancellationDoesNotPublishAnError() => WpfTestThread.RunAsync(() =>
    {
        var window = NewWindow();
        try
        {
            ((TextBlock)window.FindName("Status")).Text = "窗口正在关闭";
            ((CancellationTokenSource)typeof(WebResourceWindow).GetField("_lifetime", PrivateInstance)!.GetValue(window)!).Cancel();
            ReportFailure(window, new OperationCanceledException(Sensitive), "Parsing");
            Assert.Equal("窗口正在关闭", Status(window));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    private static WebResourceWindow NewWindow() => new(Path.Combine(Path.GetTempPath(), "ToolsBox-inspection-test-" + Guid.NewGuid().ToString("N")));
    private static string Status(WebResourceWindow window) => ((TextBlock)window.FindName("Status")).Text;
    private static void ReportFailure(WebResourceWindow window, Exception error, string stage)
    {
        var method = typeof(WebResourceWindow).GetMethod("ReportInspectionFailure", PrivateInstance);
        Assert.NotNull(method);
        var enumType = method.GetParameters()[1].ParameterType;
        method.Invoke(window, [error, Enum.Parse(enumType, stage)]);
    }
}
