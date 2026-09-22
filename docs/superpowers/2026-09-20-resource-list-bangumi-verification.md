# 资源列表自动识别与 B 站番剧单集修复验收

日期：2026-09-20。仓库：`E:\work\ToolsBox`，分支：`main`。仅修改当前工作区，不创建分支、不提交或推送；保留先前未提交的网页资源功能。

## 变更范围

- 可靠绑定的可见播放器自动发布到资源列表；同一绑定重复轮询保留行对象、顺序、清晰度选择与勾选。
- 原始网络行升级为播放器行时保持原位置和勾选；已进入下载队列的目标不被自动识别改写。
- 列表解析、下载和重试先重新读取播放器并验证导航、文档、frame、源与站点视频 ID，再读取下载 Cookie 或调用组件。
- B 站番剧仅从一个明确可见播放器及当前激活的单集链接绑定规范 `ep` 地址，标记为页面解析，不能走完整直链下载。合集 `ss` 地址缺少绑定时停止，不尝试整季。
- B 站下载沿用 yt-dlp 音视频选择和 FFmpeg 合并链，下载前核对播放器时长，输出后再验证 MP4 容器、时长及应有音轨。
- 检测 `encrypted` 事件和已存在的 `mediaKeys`；拒绝多义集号、隐藏预加载、失效确认和片段冒充完整视频。不引入 SnapWC 代码，不安装该插件，不绕过 DRM 或网站权限。
- 审查发现清空后同 URL 网络响应可以重新生成无绑定行，已在网络入列入口复用当前导航的媒体 URL 抑制；媒体观察仍更新，新播放器身份可发布，明确确认或新导航可解除抑制。

## 测试驱动与自动化

先运行新增测试确认失败，再实现对应行为：

- Core：B 站 season/episode、blob 来源、集号、可见性、DRM、唯一播放器等绑定规则；阻止 season 页回退的两项测试先失败。
- App：自动发布、重复轮询保留选择、原始行升级、清空抑制、关闭识别、隐藏或加密播放器、播放器换源、抖音 blob 和 B 站解析路由。
- MediaDownloads：预期时长非正数/非有限值、解析时长缺失或不匹配、2 秒或 2% 容差边界；39 项新增断言先失败，兼容旧调用的测试通过，再实现校验。
- InvalidDataException 的安全可读错误信息：先出现未分类提示，再增加不泄露异常细节的媒体校验失败提示。

安全回归命令：

```powershell
dotnet test ToolsBox.slnx -c Release --no-restore --filter 'FullyQualifiedName!=ToolsBox.App.Tests.Startup.SystemHandleScannerTests.CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess'
```

最终完整安全回归为 **659 项通过，0 失败**：Core 275、MediaDownloads 113、Windows 49、App 222。明确排除会关闭辅助进程文件句柄的危险测试，没有操作用户真实占用进程或句柄。较第一轮 656 项增加了 3 项清空抑制生命周期测试。

首次并发 WPF 测试出现 `PackagePart.CleanUpRequestedStreamsList` / `RemoveAt` 的进程级 BAML 缓存竞争；失败用例单独运行通过。仅在 App 测试程序集禁用并发窗口测试后，两轮完整回归通过；没有更改应用运行逻辑来掩盖异常。

## 真实 WebView2 交互

使用普通用户、独立临时配置及本地确定性媒体，不读取或清理用户现有登录配置。

最终 `ToolsBox.PlayerSmoke`：**16 项通过**，WebView2 Runtime `153.0.4234.32`。记录：`bin/verification/player-list-final-20260920/player-smoke-result.json`。包含修复后清空与新 `206` 响应的完整回归。

覆盖真实 DOM 悬浮按钮、原生确认和取消、iframe/页面/源切换失效、列表旧来源下载拒绝、自动入列表、同一行勾选保持、抖音 blob 元数据无 Content-Length、B 站当前集及多义保护、已有 mediaKeys、用户新窗口请求在原窗口导航和后退。确认下载的本地样例为 4 秒 H264/AAC MP4。

本地站点样例只能验证确定性绑定规则，不能代替真实网站结果。

审查补充回归：`player-clear-red-20260920/player-smoke-result.json` 准确复现清空后同 URL 的新 `206` 响应导致列表回弹。修复后 `player-clear-green-20260920/player-smoke-result.json` 的 4 项检查通过；该响应仍可更新已观察媒体，但不生成原始列表行。另新增 3 项 App 单测覆盖新播放器身份、显式确认和导航重置，定向测试 11/11 通过。上述结果均位于 `bin/verification/`。

## 真实抖音复测

页面：`https://www.douyin.com/jingxuan?modal_id=7680095187900697907`。

使用 `ToolsBox.PlayerProbe` 的 `capture-only` 模式，只识别、不下载、不读取下载 Cookie。记录：`bin/verification/douyin-list-20260920/player-probe-result.txt`。

- 页面有 3 个播放器，仅 1 个可见；当前视频 ID 为 `7680095187900697907`。
- 播放器时长 `438.903991` 秒，媒体绑定成功。
- 未点击悬浮按钮，资源列表已自动出现 1 条对应可下载视频。
- 接收到 10 条普通页面视频元数据。此次可见目标使用 HTTP 媒体，隐藏预加载的 blob 不被当作当前目标。
- 这是本轮列表识别证据；该视频实际下载验证见先前的播放器下载验收记录，本轮未重复下载整段。

## 真实 B 站单集下载

用户页面：`https://www.bilibili.com/bangumi/play/ss33415?from_spmid=666.4.hotlist.0`。

`ToolsBox.BangumiProbe` 使用独立匿名配置：

1. 检测一个可见 blob 播放器，时长 1494 秒，没有 mediaKeys。
2. 页面激活的第 1 集链接唯一指向 `https://www.bilibili.com/bangumi/play/ep323085`。
3. 当前集自动进入列表，`CapturedVideo=false`，通过单集页面解析；观察到的 35 个 `.m4s` 请求未作为完整直链下载。
4. 实际点击悬浮按钮打开原生确认框，验证单集链接与路由后取消。
5. 通过原生资源列表下载操作完成该单集，任务状态为“完成”。

记录：`bin/verification/bangumi-full-20260920/bangumi-result.txt`。

产物：`bin/verification/bangumi-full-20260920/downloads/1 云霄飞车杀人事件.mp4`。

| 检查 | 实际结果 |
| --- | --- |
| 文件长度 | 97,571,383 字节，约 93.05 MiB |
| ffprobe 容器 | MP4 |
| 时长 | 1493.04 秒，约 24 分 53 秒 |
| 视频 | AV1，600 × 480 |
| 音频 | AAC |

独立 ffprobe 再次确认视频和音频轨道齐全。该结果仅证明本次可公开访问的当前集，不代表全部分集、会员画质、付费或受 DRM 保护内容均支持。工具不会绕过授权。

清空修复合入工作区后，重新构建 `ToolsBox.BangumiProbe`（0 警告、0 错误），使用最终源码再次验证真实页面识别、自动入列表和原生单集确认，结果通过；不重复下载整集。记录：`bin/verification/bangumi-final-identify-20260920/bangumi-result.txt`。

## 发布与剩余边界

已顺序运行 `Publish-FrameworkDependent.cmd -NoPause` 和 `Publish-SelfContained.cmd -NoPause`，两次均成功，发布日志无警告和错误，退出码 0。旧包保留，未结束用户进程。

| 版本 | 大小 | 本轮输出（相对仓库根目录） |
| --- | --- | --- |
| 不内置 .NET | 13,762,070 字节 / 13.12 MiB | `bin/publish/framework-dependent-win-x64/20260920-151017-335-be292c00306b483189f4452cc10b0960/宝哥工具箱.exe` |
| 内置 .NET | 76,933,707 字节 / 73.37 MiB | `bin/publish/self-contained-win-x64/20260920-151108-235-2553c1b1efc84bdbb2cabf150b8d6d73/宝哥工具箱.exe` |

SHA256：

```text
不内置 .NET  178BE7C23815E0CDE8DA43F17AB61E24A2CC2D1D69E7D6A5D08A7FCBD0D1BA69
内置 .NET    EAE321A2E759302ACB68685993B37F2F6B2E9B090469659452E1E3A8A1F47FE7
```

两份最终 EXE 均使用 `--web-resources invalid invalid` 执行无窗口入口检查，均按预期安全退出（退出码 1）。该检查验证打包程序能加载并执行参数拒绝路径，不打开主界面、不请求 UAC、不读取网站配置；不等于最终包全部 UI 的人工验收。真实浏览器和媒体下载验证使用同一源码构建的专用探针。

`git diff --check` 通过。所有变更仍在 `main` 工作区，未提交或推送。

没有对全部站点、全部 B 站分集或 VIP 登录环境作全覆盖验证；没有执行危险文件句柄关闭测试；浏览器自动化不是所有真实 UI 场景的人工验收。单文件包仍不捆绑视频组件及完整 WebView2 Runtime。
