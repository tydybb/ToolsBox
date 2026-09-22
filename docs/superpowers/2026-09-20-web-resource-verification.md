# 网页资源下载验收记录

## 已完成

- 当前页面 WebView2 资源发现、图片缩略图、类型筛选、多选、纯链接复制及 TXT 导出。
- 同一 EXE 普通权限工作模式和独立窗口；主入口不变。组件首次确认下载或可信目录导入。
- yt-dlp 2026.08.19、FFmpeg 9.0.1 固定来源及 SHA256 校验，保留许可和来源记录；组件详情显示版本。
- 最高/手动质量、MP4 封装、不兼容编码转 H264/AAC、输出时长及音轨校验、同名不覆盖。
- 登录 Cookie 按域使用，临时文件 ACL 限制当前用户并清理；Referer 仅传来源站点，不传页面签名查询参数。
- 两个下载槽位、一个转换槽位、最多 100 个活动任务；重试也受重复和数量限制。退出取消并等待清理，重复关闭不跳过等待。

## 验证证据

1. 安全筛选回归：353 项通过（Core 119、Windows 49、App 155、MediaDownloads 30），0 失败。未运行实际关闭文件句柄的危险测试。
2. 真实浏览验收：`tests/ToolsBox.BrowserSmoke` 在普通桌面环境运行，WebView2 153.0.4234.32 初始化成功；本机 HTTP 图片发现、模拟 HttpOnly 登录 Cookie 和内置图片下载全流程通过。沙箱内首次初始化超时，正常桌面环境通过；这不等于管理员主程序跨权限启动已经实测。
3. 真实媒体验收：`tests/ToolsBox.MediaSmoke --install-and-test` 在全新 `bin/verification/web-media-pinned` 目录安装固定版本组件，许可证提取、校验及启用成功；MP4、HLS、DASH、FFV1/PCM MKV 转 H264/AAC、指定 DASH 格式五项均通过，退出码 0。
4. WPF 控件及布局截图、资源分类及 URL 导出、Cookie 行注入/清理、进程取消、哈希错误拒绝、路径隔离、无效媒体拒绝均有测试。
5. 两轮独立规格审查及质量审查：发现并修复时长/音轨验证、伪图片验证、关闭清理竞态、锁定组件版本、错误分类、组件详情、重试重复入队。

测试中生成的错误目录 DASH 碎片已移入 `bin/verification/web-media/stray-dash`，未删除用户文件。发布带出的 WebView2 XML 开发文档移入 `bin/verification/publish-xml`，发布配置已排除这些非运行文件。

## 复现命令

```powershell
dotnet test ToolsBox.slnx -c Release --no-restore --filter 'FullyQualifiedName!=ToolsBox.App.Tests.Startup.SystemHandleScannerTests.CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess'
dotnet run --project tests/ToolsBox.BrowserSmoke -c Release
dotnet run --project tests/ToolsBox.MediaSmoke -c Release -- --install-and-test E:/work/ToolsBox/bin/verification/web-media-pinned
```

浏览验收写入结果到该项目输出目录的 `browser-smoke-result.txt`，需读取 PASS/FAIL，不能只依赖 WinExe 宿主退出码。媒体验收需要网络访问以首次下载组件；只用本机生成的视频样例，不使用真实账号。

## 未覆盖及边界

### 普通权限启动修复补充

用户报告管理员主界面无法启动浏览窗口后，新增 `tests/ToolsBox.WebLaunchSmoke`。使用 UAC 启动管理员诊断父进程，原始代码稳定复现 `CreateProcessWithTokenW` 错误 5。仅取消 LOGON_WITH_PROFILE 仍失败；恢复该参数并将 DuplicateTokenEx 的访问掩码从 0x000B 改为 MAXIMUM_ALLOWED 后通过，子进程实测为非管理员且退出码 0。该标志只决定令牌句柄的可用访问权限，不提升桌面用户令牌的权限或完整性级别。

主窗口错误提示现在保留失败步骤和 Windows 错误码，不再把所有失败归因于资源管理器或路径。未修改系统 ACL，也未结束用户进程。

- 尚未人工验收最终单文件 EXE 从管理员主界面跨权限启动的 UAC/窗口交互，也未逐站验收真实网站账号、验证码或反爬行为。
- 不绕过 DRM，不能保证所有网站；部分站点需要额外 JavaScript 引擎或站点适配。无下载权限不作规避。
- 图片使用结构校验，不是完整解码；动态 WebP 和不支持的图片类型会拒绝。视频不是逐帧校验。
- 来源 Referer 仅保留站点，要求精确页面 Referer 的站点可能失败；签名 URL 本身仍属敏感内容。
- 发布 EXE 内置 .NET，不内置 WebView2 Runtime 或视频引擎；首次使用需要组件环境。

## 环境检测与交互改进验收（2026-09-20）

- 安全筛选回归：368 项通过（Core 119、Windows 49、App 167、MediaDownloads 33），排除会实际关闭句柄的 `CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess`。
- 测试先行：导航样式、勾选状态、环境入口测试最初失败，实现后通过；覆盖筛选全选、三态表头、隐藏选择保留。
- 浏览器实测 PASS：普通权限 WebView2 153.0.4234.32、本机 HTTP 资源发现、仅勾选图片下载并携带登录 Cookie；隐藏已选视频不进入下载队列；Stable 运行时检测成功。
- 已有固定组件健康检查 PASS，真实 MP4/HLS/DASH/FFV1+PCM 转码/指定 DASH 格式五项下载再次通过。本机 HTTP 监听在受限沙箱失败，普通桌面环境复验成功。
- 实际渲染检查：首页导航按钮统一，1000×700 资源列表筛选与复选框正常，620×500 环境窗口展开详情及按钮无裁切。
- 安装测试覆盖：已有 Runtime 跳过、签名失败拒绝执行、取消前不启动、异步安装 gate 验证取消后继续等待安装器、安装失败或未检测到版本报错、注册表残留/预览通道不误报 Stable Runtime、组件损坏不报告可用、下载字节进度。
- **没有在本机卸载或安装 WebView2 来进行验证。** 安装编排使用 fake backend 测试；真实缺失环境上的微软安装器、网络代理及系统策略差异仍需现场验收。没有停止用户应用或修改系统权限。
- 新自包含发布目录：`bin/publish/web-resources-environment-win-x64/`；源代码保留在 main 工作区，未提交或推送。
- 审查修复：运行时检测同时核对注册信息及 Stable loader API；主界面仅凭文件存在时标记“文件已安装（可检测验证）”，不误报就绪。需求和质量复审无剩余发现。
- 安装参数及权限依据：https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution

## 依据

- WebView2 资源响应事件：https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.webresourceresponsereceived
- 普通用户令牌启动：https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createprocesswithtokenw
- yt-dlp：https://github.com/yt-dlp/yt-dlp
- FFmpeg Windows 构建：https://www.gyan.dev/ffmpeg/builds/
