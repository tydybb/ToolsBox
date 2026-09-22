# 网页资源下载 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在普通权限独立窗口完成当前网页资源识别、链接导出和下载。

**Architecture:** 浏览和下载使用同一 EXE 的 --web-resources 普通权限工作模式，复用单文件发布能力；管理员入口通过桌面 shell 的普通用户令牌启动，子进程再次检查权限并拒绝管理员执行。Core 提供资源识别与导出规则，独立 MediaDownloads 项目提供组件供应、解析及下载服务。

**Tech Stack:** .NET 8、WPF、WebView2、HttpClient、yt-dlp、FFmpeg/ffprobe。

## 执行与验收清单

- [ ] 1. 下载服务：新增 src/ToolsBox.MediaDownloads 和 tests/ToolsBox.MediaDownloads.Tests。先测 URL/文件名、组件 SHA256、进程参数、取消、输出校验；实现可信 HTTPS 下载/导入、仅当前视频解析、最高质量/手动格式、MP4 合并与转码和直接图片下载。所有进程使用 ArgumentList、禁用用户引擎配置和插件；引擎只在普通权限进程运行。测试不得操作用户进程或真实登录账号。
- [ ] 2. 资源域规则：先在 Core.Tests 新增资源分类、片段过滤、去重、纯链接导出测试；实现 Core/WebResources 中对应类型。只接收 HTTP(S)，拒绝 URL 用户名密码；保留签名查询参数，不把 blob 和分段文件当完整视频。
- [ ] 3. 浏览启动：先测普通权限参数和管理员拒绝入口；实现 App/WebResources/WebResourceLauncher.cs、App 启动分支，主导航入口。父进程保持活动句柄，不按名字寻找/终止工作进程；工作进程关闭时处理本地任务。
- [ ] 4. 浏览窗口：新增 WebResourceWindow.xaml/.cs，WebView2 请求事件读取当前页面图片/视频响应，导航分组；阻止非 HTTP(S) 导航、弹窗外跳、原生自动下载、敏感设备权限。不得注入宿主对象。缺失 Runtime 明确提供安装链接。
- [ ] 5. 下载交互：绑定服务，目录选择、分类筛选、多选、缩略图、最高质量和清晰度选择、复制/导出警告、组件安装确认及导入；队列两个下载、一个转码，取消和重试，失败不假装完成；登录态严格按 URI 域作用域传递。
- [ ] 6. 验证：新增本地样例与 WPF 集成测试；运行安全筛选全量测试和 Release 发布；审查 spec 后审查质量，修复所有重要问题；更新 README 和验证说明，准确记录未完成的真实网站/UAC 验收。

## 测试命令

```powershell
dotnet test tests/ToolsBox.Core.Tests -c Release --filter FullyQualifiedName~WebResource
dotnet test tests/ToolsBox.MediaDownloads.Tests -c Release
dotnet test ToolsBox.slnx -c Release --filter 'FullyQualifiedName!=ToolsBox.App.Tests.Startup.SystemHandleScannerTests.CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess'
git -c core.safecrlf=false diff --check
```

每项测试先记录缺少实现的失败，再记录通过。实现文件依职责拆分，不让浏览窗口承担网络下载实现。用户已明确同意 main 和独立窗口，不新建分支、不自动推送。
