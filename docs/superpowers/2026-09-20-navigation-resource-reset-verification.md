# 网页切换与刷新后的资源列表隔离

日期：2026-09-20。工作区：`E:\work\ToolsBox`，`main`，未提交/推送。

## 根因及修复

- `NavigationStarting` 原先只清播放器缓存，不清 `_resources`；`SourceChanged` 原先只更新网址，站内 SPA 路由也会留下旧行。
- 全页导航及同网址刷新现在统一重置列表、焦点、勾选及播放器文档代次；站内 URL 变化重置列表和代次，仍允许当前 DOM 播放器重新绑定。拒绝非 HTTP(S) 导航发生在重置之前；iframe 导航不清整个父页列表。
- 保留独立的下载任务队列，不清 Cookie、收藏或历史数据，不取消已有下载。
- 新增请求归属跟踪：请求发起时记录方法、URL 和页面代次，响应返回时核对；先消费响应记录，再判断状态码和是否启用识别。旧请求晚到、未知请求、跨代同 URL 并发歧义都保守拒绝，不用不可靠的 Referer 猜测。
- 最多保存 8192 个未完成请求键。没有响应的失败请求不猜测过期；容量溢出后停止接收新捕获结果并提示重开浏览窗口，避免丢失请求记录后误归属。不会修改网页请求头、URL 或网站内容。
- 实测 WebView2 刷新可能复用视频缓存，不再次发出对应网络响应。因此只保留最多 2000 条已观察媒体的 URL/类型证据，必须在新文档中重新找到可见播放器并精确匹配；不保留旧资源行，跨完整文档不保留抖音元数据。证据上限采用淘汰旧项，不无限增长。

## 验证

1. 新增 WPF 导航测试先失败：重置后视频和图片仍在列表；修复后通过，并检查任务对象保留。
2. 真实 WebView2 先失败：`bin/verification/navigation-red-20260920/player-smoke-result.json`，`Reload retained rows from the previous document.`。
3. 缓存视频重新绑定测试先失败（新文档列表为空），保留有界来源证据后通过。真实本地请求日志证实刷新没有再次收到 `/first.mp4`，不能只用新网络响应重建播放器资源。
4. 请求跟踪 13 项单测先 RED（10 失败 / 3 通过），实现后 13/13 通过。覆盖同代并发、晚到响应、跨代乱序歧义、顺序 URL 复用、未完成请求、容量饱和及参数边界。
5. 完整安全回归 **674 项通过**：Core 288、MediaDownloads 113、Windows 49、App 224。命令：

   ```powershell
   dotnet test ToolsBox.slnx -c Release --no-restore --filter 'FullyQualifiedName!=ToolsBox.App.Tests.Startup.SystemHandleScannerTests.CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess'
   ```

   排除真实关闭辅助进程句柄的危险测试，未操作用户占用进程或句柄。

6. 完整真实 WebView2 本地回归 **21 项通过**：`bin/verification/navigation-complete-20260920/player-smoke-result.json`。新增覆盖：同 URL 刷新、SPA 重绑、跳转前延迟 PNG 响应、目的页延迟加载、前后退、任务保留。原有原生下载确认、过期来源拒绝、iframe、抖音 blob、B 站单集、206 清空抑制及 4 秒 H264/AAC MP4 下载验证继续通过。

所有浏览器探针使用独立临时配置，不读取现有网站登录资料。本地 fixture 和探针诊断文件均在 Git 忽略的 `bin/verification`。

真实网站补充识别复测也通过：

- 抖音用户视频 `7680095187900697907`：唯一可见播放器、时长 438.903991 秒，自动入列表 1 条；`bin/verification/navigation-douyin-20260920/player-probe-result.txt`。本轮 `capture-only`，不读取下载 Cookie 或下载整段视频。
- B 站 `ss33415`：当前集仍绑定 `ep323085`，1494 秒，自动列表按单集页面解析，原生确认框正常；`bin/verification/navigation-bangumi-20260920/bangumi-result.txt`。确认后取消，本轮不重复下载。

独立审查还提出“NavigationStarting 后旧文档继续发请求”的潜在窗口。追加真实慢导航样例，旧页用定时器发唯一 PNG 请求，分别验证默认 fetch 与 `keepalive:true`，两轮各 5 项通过（`navigation-pending-review-20260920`、`navigation-keepalive-review-20260920`）。后一轮计数明确限定在宿主已收到 NavigationStarting 之后，确有旧页请求，但本运行时没有向工具继续发送这些请求的响应事件，也没有污染目标页列表。该静态风险未在当前 Runtime 153.0.4234.32 复现；不把此结果推广为所有浏览器版本/后台 worker 的保证，也未为未证实场景追加可能漏掉新文档早期资源的全局拦截。

诊断项目的 `--no-restore` 构建曾报告既有 NuGet 漏洞源不可达的 NU1900 缓存警告；无编译错误。安全测试和正常发布使用成功的依赖环境。`git diff --check` 通过。

## 发布

已完成独立审查及补充慢导航验证。两份一键脚本顺序发布成功，退出码 0；未覆盖旧包或关闭用户程序。本次仍不承诺所有网站/DRM/付费资源可下载。

| 版本 | 大小 | 输出路径（相对仓库） |
| --- | --- | --- |
| 不内置 .NET | 13,762,070 字节 / 13.12 MiB | `bin/publish/framework-dependent-win-x64/20260920-153921-876-a47d1d97d6494cd1a74974f8ca1b6bf2/宝哥工具箱.exe` |
| 内置 .NET | 76,934,639 字节 / 73.37 MiB | `bin/publish/self-contained-win-x64/20260920-154334-272-32bff875cd0f47c78b7f1acac5ab6bfe/宝哥工具箱.exe` |

SHA256：

```text
不内置 .NET  5BA966F893C43076EE823780E000F39DE2FE8E0D2ABAE6577B9D4B43CFCD516C
内置 .NET    BB7530A8AD72B546A3A3CB6B87F71C4E54E85D1BF6FA6A6BD1EFEBAED3D9A630
```

两份最终 EXE 均通过 `--web-resources invalid invalid` 无窗口入口检查，预期退出码 1；没有打开主界面或请求 UAC。此检查不是全部打包 UI 的人工验收，真实浏览器回归使用同一生产源码的专用探针。
