# 播放器资源列表与 B 站单集下载修复计划

> **For agentic workers:** 使用测试驱动、并行独立子任务与完成前验证；用户要求直接在 main 实施，不创建分支，不提交或推送。

**Goal:** 抖音已可靠绑定的视频自动入列表；B 站番剧绑定当前单集并经现有下载链输出带声音的 MP4。

**Architecture:** 自动识别只发布资源、不读取 Cookie 或自动下载。列表和悬浮按钮共用资源发布，列表下载前复核播放器身份。B 站以可见播放器的明确当前集 ID 转为 ep 链接，交给 yt-dlp 下载音视频、FFmpeg 合并，保持分片过滤和 DRM 拒绝。

**Tech Stack:** .NET 8、WPF、WebView2、xUnit、yt-dlp、FFmpeg。

设计依据为已提交用户阅读并获“修复和实施”确认的 `docs/research/2026-09-20-snapwc-analysis.md`。优先修复实际用户场景，不引入 SnapWC 代码、插件运行或解析服务器。

## 1. B 站绑定（独立子任务）

- [x] Core.Tests 补充 season 页面 + 可见 blob + ep323085 绑定、ID 不一致／多义／DRM 拒绝测试，先运行失败。
- [x] `PlayerMediaChoice(string Url, string Label, WebResourceKind Kind, bool RequiresPageExtraction = false)` 区分页面提取与完整媒体。
- [x] `PlayerOverlayScript.cs` 从当前集链接识别 B 站 SiteId；`PlayerMediaBinding.cs` 仅产生规范单集地址。
- [x] `dotnet test tests/ToolsBox.Core.Tests -c Release` 通过。

## 2. 列表发布与下载复核（主线程）

- [x] App.Tests 复现重复发布、原始行升级、保留勾选、清空后不立即重新出现、B 站路由问题；先运行失败。
- [x] 新增局部类 `WebResourceWindow.PlayerResources.cs`：`PublishPlayerResources(frame, document)` 使用现有 Resolve；每个可靠来源一行，列表顺序稳定、不重复。
- [x] 每行记录 `PlayerDownloadCandidate` 和原始 choice，缓存只在播放器身份不变时更新；队列中的目标不随自动识别改变。
- [x] `PollPlayers` 调用发布；悬浮确认复用同一发布方法；清空列表记录当前来源抑制项，导航重置。
- [x] 列表下载及解析在用户操作后重新读取对应播放器；来源变化则不启动。无 Cookie 自动读取、无网页脚本自动授权下载。
- [x] App.Tests 定向通过；保留原有页面原生下载确认及任务重试行为。

## 3. 单集时长约束（独立子任务）

- [x] MediaDownloads.Tests 补充预期时长缺失／无效／不匹配／容差边界测试并运行失败。
- [x] `DownloadVideoAsync(..., double? expectedDurationSeconds = null)` 在提取信息后、实际下载前核对时长，容差 `Math.Max(2, expected * 0.02)`。
- [x] 主线程传递已绑定播放器的时长；现有无绑定调用保持兼容。
- [x] MediaDownloads.Tests 通过。

## 4. 验收与交付

- [x] 隔离 WebView2 配置执行 PlayerSmoke，覆盖自动入列表、重复／清空／过期来源、原生确认及取消。
- [x] BangumiProbe 验证用户 ss33415 页面识别 ep323085，执行单集下载，ffprobe 验证视频+音频与时长。
- [x] 独立需求审查与代码质量审查，修复发现的问题。
- [x] 安全回归：`dotnet test ToolsBox.slnx -c Release --filter 'FullyQualifiedName!=ToolsBox.App.Tests.Startup.SystemHandleScannerTests.CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess'`。
- [x] 更新 README 和验证记录，运行两份既有打包脚本。保留旧包，不结束用户进程，不操作真实文件解锁。

最终结果：安全回归 659 项通过；PlayerSmoke 16 项通过；真实抖音自动入列表、真实 B 站 ep323085 双轨 MP4 下载与最终源码再次识别通过。审查发现的清空后新 206 响应回弹问题已通过 RED/GREEN 复现修复；App 测试禁用并发以避免 WPF 进程级 BAML 缓存竞争。详情与两份 EXE 校验值见 `docs/superpowers/2026-09-20-resource-list-bangumi-verification.md`。

验收边界：没有验证过的站点／VIP／DRM 不宣称支持；现场站点失败时如实记录错误，不以编译通过代替下载成功。
