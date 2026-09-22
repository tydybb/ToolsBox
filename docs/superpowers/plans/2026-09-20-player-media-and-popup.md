# 播放器媒体识别与新窗口跳转实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. 本次实施与验收已结束，证据见 `../2026-09-20-player-media-verification.md`。

**Goal:** 实施用户确认的 A 方案，识别当前播放器并原生确认下载；修复 B 站用户点击的新窗口链接被无条件拦截。

**Architecture:** 浏览器注入受限 DOM 观察脚本，宿主定时读取快照并接收仅含播放器标识的点击消息；原生确认是下载授权边界。普通 HTTP 播放器精确匹配已观测资源；抖音 blob 播放器使用其最近单播放器容器的视频编号，与当前文档中浏览器正常收到的抖音媒体 JSON 对应。下载复用现有队列、凭据隔离、MP4 转换与校验，不调用抖音页面解析器。

**Tech Stack:** .NET 8 WPF、WebView2、System.Text.Json、JavaScript DOM、现有 yt-dlp / FFmpeg 组件。

---

## 依据与边界

- 已核对真实 DOM：用户视频的 `video` 位于单播放器容器内，最近容器中的话题链接 `aweme_id=7680095187900697907` 和外层 `video_7680095187900697907` 一致；预加载下一条具有不同编号且不可见。当前视频 `currentSrc` 为 blob，时长 438.9 秒。
- 实施前程序仅按地址解析，未实现上述 DOM/媒体对应关系；当时独立 yt-dlp 匿名调用未获取到该条元数据。不能据此认定用户没有登录。本次已改用播放器绑定路径，并完成指定样例的实际 MP4 下载。
- 不读取系统浏览器 Cookie、不破解 DRM、不绕过验证码、不抓取隐藏账号状态、不自动下载。只读取工具网页的正常媒体响应；日志不落 Cookie、签名 URL 或完整页面数据。
- 新窗口优先采用用户点击链接在当前工具网页跳转的轻量策略，HTTP(S) 以外协议和无用户手势的自动弹窗继续阻止；用户已确认采用当前工具网页跳转。
- 在 main 保留全部既有未提交改动，不创建分支、不提交、不推送、不删除旧包。

## Task 1：站点媒体元数据（Core）

**Files:** `src/ToolsBox.Core/WebResources/DouyinMediaParser.cs`、对应 Core 测试。

- [x] 先用合成 detail/feed JSON 测试：精确视频编号、清晰度地址、去重、格式/URL/编号限制、DRM 拒绝、无效/超长 JSON；记录 RED。
- [x] 实现纯解析接口，不发网络请求：

```csharp
public sealed record DouyinMediaVariant(string Url, string Label, int Height, long BitRate);
public sealed record DouyinMediaItem(string Id, string Title, double? DurationSeconds, IReadOnlyList<DouyinMediaVariant> Variants);
public static class DouyinMediaParser
{
    public static bool IsMetadataResponse(string responseUrl);
    public static IReadOnlyList<DouyinMediaItem> Parse(string json);
}
```

- [x] 仅从已观测的抖音站点媒体 API 响应中提取 `aweme_id`、`desc`、`video.duration`、`video.play_addr` / `video.bit_rate[].play_addr`；不使用相关推荐顺序作绑定。验证 GREEN。

## Task 2：已识别媒体直接下载（MediaDownloads）

**Files:** `src/ToolsBox.MediaDownloads/CapturedVideo.cs`、对应 MediaDownloads 测试。

- [x] 先测试 HTTP(S) 校验、进度/取消、请求凭据范围和完整媒体验证；不以“进程退出 0”代替成品校验。
- [x] 新增不调用抖音页面解析的入口，尽量复用已有 MP4 处理：

```csharp
public Task<string> DownloadCapturedVideoAsync(Uri uri, string outputDirectory,
    string title, double? expectedDurationSeconds = null,
    IProgress<DownloadProgress>? progress = null, CancellationToken ct = default,
    IReadOnlyList<MediaCookie>? cookies = null, string? referer = null, string? userAgent = null);
```

- [x] 保留 Cookie 域/路径/过期/安全限制、禁止同名覆盖、取消清理与最终 ffprobe 校验。流清单仍用现有下载路径，不宣称任意 blob 可直接下载。

## Task 3：播放器脚本、宿主绑定与原生确认（App）

**Files:** 新增 `WebResourceWindow.Players.cs`、`PlayerOverlayScript.cs`、`PlayerDownloadDialog.cs`；定点修改窗口和资源行。

- [x] 测试先行：同页多播放器、隐藏/预加载、相同元素换源、歧义编号、导航后旧目标、伪造消息不得自动下载。
- [x] DOM 脚本为视频元素生成本页标识与源代次，显示左上按钮并跟随布局；快照包含可见性、source、时长、准确站点编号，不包含账号数据。
- [x] 点击消息只传标识，不接收任意文件路径/URL；宿主重读当前快照和来源、显示原生确认，取消不读下载 Cookie、不创建任务。
- [x] 浏览器正常媒体 JSON 响应按文档代次保存有界索引；播放器编号精确匹配索引。没有可靠来源则说明限制，不能猜“最后请求的资源”。
- [x] 原生面板确认来源、清晰度和目录后，重新检查导航/播放器源未变，再进入队列；已有当前页按钮复用相同识别逻辑。
- [x] 清理计时器、文档脚本、frame、弹出/放回和关闭生命周期；保留 host objects 关闭及网站直接下载拦截。

## Task 4：新窗口链接与完整验收

**Files:** 定点修改 NewWindowRequested、Core 策略及测试；更新 BrowserSmoke / 独立播放器探针、README。

- [x] TDD 验证用户点击 HTTP(S) 链接可导航、自动弹窗/危险协议拒绝，保留后退和同一 WebView2 登录环境。
- [x] 本机真实 WebView2 验证播放器按钮、多源/换源和新窗口事件；本机合成视频下载验证 MP4 内容完整。
- [x] 用用户抖音链接验收媒体识别及实际下载；有站点限制时记录具体停在哪一步，不能声称全站支持。B 站点击页面实测与本机 target=_blank 测试分别记录。
- [x] 运行安全筛选回归，排除真实关闭句柄测试；独立需求/质量审查后，使用两个新入口顺序发布并报告真实产物。

```powershell
dotnet test ToolsBox.slnx -c Release --filter 'FullyQualifiedName!=ToolsBox.App.Tests.Startup.SystemHandleScannerTests.CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess'
.\Publish-FrameworkDependent.cmd -NoPause
.\Publish-SelfContained.cmd -NoPause
```
