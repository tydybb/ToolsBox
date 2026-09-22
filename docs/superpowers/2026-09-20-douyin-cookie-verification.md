# 抖音页面解析 ArgumentException 排查记录

## 复现与根因

- 用户入口：打开 `https://www.douyin.com/` 后点击“解析当前页面视频”。
- 在独立 WebView2 目录中访问主页，不使用用户已有的登录目录。第一次导航结束时只有 3 个 Cookie，没有异常；等待普通客户端导航完成后有 34 个 Cookie，其中 1 个名称为空。域名、路径和控制字符检查均正常。
- 浏览器 Cookie 转换成功，随后 `CookieFile.Create` 的“名称为空即拒绝”校验抛出 `ArgumentException`。当时尚未启动 yt-dlp，因此不是下载引擎、管理员权限或 DRM 导致的该异常。
- 最小修复：允许名称为空、值非空的浏览器 Cookie，保留 Netscape 文件空名称字段；名称和值同时为空、控制字符、无效域名和路径仍拒绝。没有删除 Cookie、清除登录态或放宽域/路径范围。

## 验证证据

- 新增空名称回归先失败，修复后 Cookie 文件相关 12 项测试通过；控制字符注入、空条目和无效域/路径继续被拒绝。
- 使用最终校验代码再次访问独立抖音主页：34 个 Cookie / 1 个空名称，Cookie 转换和临时文件创建均通过；引擎随后返回 `MediaDownloadException.ParseFailed`，不再是 `ArgumentException`。
- 主页并非具体视频地址。上述复验只证明参数异常修复，**不代表抖音具体视频解析或下载已经通过**；仍受站点支持、有效登录与访问权限影响。
- `tests/ToolsBox.DouyinProbe` 只记录阶段、计数、异常类型及方法名，不记录 Cookie 名称/值、User-Agent、完整异常消息、子进程 stderr 或签名地址。
- 原始结果：`bin/verification/douyin-argument-settled/diagnostic-result.txt`；最终 Cookie 复验：`bin/verification/douyin-cookie-final/diagnostic-result.txt`。
- 真实 yt-dlp 本机认证媒体验收 4 项通过，退出码 0：解析请求和实际 MP4 下载均携带精确的普通 Cookie 与无名称裸值；错误路径、错误主机均不发送这些 Cookie。只用本机合成值与生成的视频，不使用真实账号。
- 直链 MP4 可以不提供可选清晰度列表。初版集成测试错误地要求格式列表非空，虽认证请求已通过仍报告失败；核对计数后移除这一无关断言，保留精确请求内容、输出文件及隔离断言。
- 最终安全筛选回归 448 项通过：Core 154、Windows 49、App 197、MediaDownloads 48；仍排除实际关闭文件句柄的测试。
- 真实 WebView2 本机浏览验收通过并正常退出：保留已有浏览/下载/导出/弹出窗口检查，新增 `history.pushState` 在没有整页导航时同步地址栏；即使人为使缓存地址过期，解析仍使用实时 Source 并保留实际 PageUrl。
- 抖音主页实测自动跳转 `/jingxuan`，已补充该精选页提示和测试。最终 `bin/verification/douyin-complete-final/diagnostic-result.txt` 中 `homepage-button-guidance=True`，Cookie 两阶段 PASS。探针随后故意直接调用底层主页解析以验证 Cookie，不代表界面仍会把主页提交给引擎。
- 独立代码复查未发现需修复问题；没有实测具体抖音视频下载、真实账号登录或最终 EXE 的 UAC 交互。

## 发布

- 不含 .NET：`bin/publish/douyin-fix-framework-win-x64/宝哥工具箱.exe`。
- 内置 .NET：`bin/publish/douyin-fix-self-contained-win-x64/宝哥工具箱.exe`。
- 两份 Release 发布均成功；已有视频组件和登录目录不变，不需要因本次更新重新安装组件或清空登录数据。
- 初次沙箱依赖还原出现 NuGet 漏洞数据网络检查警告；在允许联网环境强制重新还原后消失，最终发布没有该警告。
- 源代码保留 main 工作区，未提交、推送或删除旧发布包。

## 复现命令

```powershell
dotnet test tests/ToolsBox.MediaDownloads.Tests -c Release --no-restore --filter FullyQualifiedName~CookieFileTests
dotnet run --project tests/ToolsBox.DouyinProbe -c Release -- <独立测试目录> <已安装的组件根目录>
dotnet run --project tests/ToolsBox.MediaSmoke -c Release -- --test-cookies bin/verification/web-media-pinned
```

探针为 WinExe，必须读取测试目录内的 `diagnostic-result.txt`，不能只依据进程退出码。其浏览目录与软件日常登录目录不同，不要传入真实用户数据目录。未结束用户进程或修改系统权限。

## 兼容依据

- [Chromium Cookie 序列化](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/net/cookies/canonical_cookie.cc)：无名称 Cookie 发送原值，不添加 `=`。
- [CPython MozillaCookieJar](https://github.com/python/cpython/blob/3.12/Lib/http/cookiejar.py)：读取 Netscape 空名称字段并恢复裸值。
- [yt-dlp Cookie 文件处理](https://github.com/yt-dlp/yt-dlp/blob/2026.08.19/yt_dlp/cookies.py)：沿用 MozillaCookieJar 读取语义。
