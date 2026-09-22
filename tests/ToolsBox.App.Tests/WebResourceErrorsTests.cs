using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using ToolsBox.App.WebResources;
using ToolsBox.MediaDownloads;

namespace ToolsBox.App.Tests;

public class WebResourceErrorsTests
{
    private const string Sensitive = "Cookie=session-secret https://private.example/video?token=secret C:\\Users\\private\\cookies.txt";

    [Fact]
    public void MediaValidationFailureHasSafeActionableMessage()
    {
        string message = WebResourceErrors.Describe(new InvalidDataException(Sensitive));
        Assert.Contains("校验", message);
        Assert.Contains("播放器", message);
        Assert.DoesNotContain("secret", message);
        Assert.DoesNotContain("磁盘空间", message);
    }

    [Fact]
    public void RawDiagnosticMessagesNeverAppearInUserErrors()
    {
        Exception[] errors =
        [
            new JsonException(Sensitive), new Win32Exception(5, Sensitive),
            new UnauthorizedAccessException(Sensitive), new IOException(Sensitive),
            new FileNotFoundException(Sensitive, Sensitive),
            new HttpRequestException(Sensitive, null, HttpStatusCode.Forbidden),
            new InvalidOperationException(Sensitive), new ArgumentException(Sensitive)
        ];
        foreach (var error in errors)
        {
            var message = WebResourceErrors.Describe(error);
            Assert.DoesNotContain("secret", message);
            Assert.DoesNotContain("private", message);
            Assert.DoesNotContain("Cookie", message);
            Assert.DoesNotContain("https://", message);
        }
    }

    [Fact]
    public void SafeErrorCategoriesRetainUsefulEvidence()
    {
        Assert.Contains("Windows 错误 5", WebResourceErrors.Describe(new Win32Exception(5, Sensitive)));
        Assert.Contains("信息格式无效", WebResourceErrors.Describe(new JsonException(Sensitive)));
        Assert.Contains("访问被拒绝", WebResourceErrors.Describe(new UnauthorizedAccessException(Sensitive)));
        Assert.Contains("Forbidden", WebResourceErrors.Describe(new HttpRequestException(Sensitive, null, HttpStatusCode.Forbidden)));
        var unknown = WebResourceErrors.Describe(new InvalidOperationException(Sensitive));
        Assert.Contains("InvalidOperationException", unknown);
        Assert.Contains("尚未确定原因", unknown);
        Assert.DoesNotContain("DRM", unknown);
        var typed = new MediaDownloadException(MediaDownloadError.LoginRequired);
        Assert.Equal("失败：" + typed.Message, WebResourceErrors.Describe(typed));
    }
}
