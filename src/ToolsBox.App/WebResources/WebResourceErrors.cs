using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using ToolsBox.MediaDownloads;

namespace ToolsBox.App.WebResources;

public static class WebResourceErrors
{
    // Only our fixed messages and numeric/type identifiers may leave this boundary.
    // Raw exception messages can contain Cookie values, signed URLs or local paths.
    public static string Describe(Exception error) => error switch
    {
        MediaDownloadException e => "失败：" + e.Message,
        FileNotFoundException => "失败：视频组件缺失，请先安装或导入。",
        HttpRequestException e => $"失败：网络请求错误（{e.StatusCode?.ToString() ?? "连接失败"}），链接可能过期或无权访问。",
        JsonException => "失败：解析组件返回的媒体信息格式无效（JsonException）。请检查组件版本后重试。",
        Win32Exception e => $"失败：组件进程操作失败（Windows 错误 {e.NativeErrorCode}），请检查组件安装和运行权限。",
        UnauthorizedAccessException => "失败：访问被拒绝，请检查组件、临时目录及输出目录权限。",
        InvalidDataException => "失败：媒体信息或完整性校验未通过，时长可能与当前播放器不符，已停止下载。请重新识别当前视频。",
        IOException => "失败：文件写入或验证失败，请检查磁盘空间及目录权限。",
        _ => $"失败：尚未确定原因（{error.GetType().Name}）。请记录执行的操作及此错误类型以便排查。"
    };
}
