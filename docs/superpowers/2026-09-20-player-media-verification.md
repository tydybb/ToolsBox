# 播放器下载与新窗口跳转验收

## 实现范围

- 播放器左上角 DOM 按钮；点击后由 WPF 原生窗口确认视频、来源、清晰度和目录，确认前不读取下载 Cookie、不创建任务。
- 当前视频真实 HTTP(S) 来源与浏览器观测资源精确匹配；抖音 blob 使用单播放器容器的视频编号与正常媒体 JSON 对应，并复核时长。
- 文档、iframe、媒体源代次与 SPA 地址切换使旧确认失效；有遮罩挡住播放器时不误显示可操作按钮。
- 捕获的完整媒体直接下载并封装/转换 MP4，不再交给抖音页面提取器；输出校验容器、时长与已知音轨。
- 用户点击引发的新窗口 HTTP(S) 链接在原 WebView 中跳转，自动弹窗和危险协议仍拒绝。
- 不增加激活、许可证、作者身份或证书操作；没有 Git 提交/推送，也没有清除原网站资料。

## 真实验证（2026-09-20）

### 本机 WebView2 + 合成媒体

`tests/ToolsBox.PlayerSmoke` 使用隔离资料目录、本机 HTTP 服务和 FFmpeg 生成的 4 秒 H264/AAC 视频。实际执行 WebView2 鼠标点击及 WPF 原生确认，不以纯规则测试替代 UI 验证。

最终结果 `bin/verification/player-local-final-314c9fe7e8d94e02ae7ea8ab2ed8f9d9/player-smoke-result.json`：10 项通过，运行时 153.0.4234.32。

- 主页面/iframe 按钮与原生确认；隐藏预加载播放器排除。
- iframe 伪造来源拒绝；iframe 开始导航后旧确认拒绝。
- 取消不创建任务；同一元素换源、代次改变和 SPA 地址改变拒绝旧确认。
- 确认后进入 CapturedVideo 路径，实际输出 MP4 校验为 4 秒、H264 视频 + AAC 音频。
- 无用户手势的页面 `window.open` 事件实际被拒绝。
- 真实鼠标点击 `target=_blank` 产生用户手势事件，在相同 CoreWebView2 导航，后退成功。
- 没有 `Content-Length` 的媒体 JSON 响应解析成功；合成 blob 播放器按编号 123、时长 4 秒绑定正确来源。该 fixture 正常消费 fetch JSON 响应，仅生产观察者读取 `GetContentAsync`。

另外通过 `ToolsBox.MediaSmoke --test-captured` 实测 VP8/Vorbis WEBM → H264/AAC MP4，两次输出均为 4.000 秒；第二次同名输出自动加 `(1)`，首个文件 SHA256 不变，临时目录清理完成。

### B 站

隔离普通用户测试浏览器访问 B 站首页，鼠标点击实际视频卡片。结果 `bin/verification/player-bilibili-20260920-a/player-probe-result.txt`：用户触发新窗口、当前页打开视频、相同 Core、可以后退四项均为 True。此项不代表 B 站全部视频下载验证。

### 用户提供的抖音视频

目标编号 `7680095187900697907`。隔离资料目录下正常关闭网站可关闭的登录提示与新手引导后，实际播放器 HTTP 来源绑定成功。没有读取用户现有浏览器账号资料，也没有绕过登录/验证码。

`bin/verification/player-douyin-20260920-e/player-probe-result.txt` 记录绑定成功和实际下载校验通过；独立 ffprobe 复核：

- MP4，1920 × 1080，H264 视频 + AAC 音频。
- 时长 438.903991 秒，大小 80,574,051 字节。
- 输出位于该测试目录的 `downloads/douyin-user-video-verification.mp4`。

后续只识别验证 `player-douyin-20260920-g` 捕获到 10 条实际媒体元数据。缺少 `Content-Length` 的响应必须先检查响应头是否存在；多个诊断观察者不要并发读取同一个响应流。该后续运行又出现网站遮罩，故没有把其不可见播放器视为可下载目标。

## 自动测试与审查

- 新增纯规则测试先失败再通过：播放器精确绑定/失效、抖音 JSON 边界、新窗口策略。
- 下载测试覆盖重定向 Cookie 范围、无名称 Cookie、取消、大小/响应类型限制与安全错误。
- 安全筛选回归：Core 248、MediaDownloads 73、Windows 49、App 210，共 580 项通过。
- 明确排除会实际关闭其他进程句柄的 `SystemHandleScannerTests.CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess`。
- 需求与质量独立审查指出的 HTTP 安全上下文、iframe 来源/代次、缓存代次竞态和单视频备用解析问题均已修复并复核。
- 最终源代码重新运行安全筛选回归仍为 580 项全部通过；最终发布零编译错误。较早受限网络还原出现过 NU1900，允许联网的最终发布还原未再报告该警告。

## 本次发布

使用根目录两个一键脚本，均退出 0；不覆盖旧包。

- 不含 .NET：`bin/publish/framework-dependent-win-x64/20260920-135957-382-caf06ff01c2a4d038d19765462450dd7/宝哥工具箱.exe`，13,741,590 字节（13.11 MiB）。SHA256：`CFF95EF5F38DBC642C21327A14C95591CB83283FB3AB059C6EEC94C461F5399A`。
- 内置 .NET：`bin/publish/self-contained-win-x64/20260920-140014-582-325b2736a1af4988987b34c81229331d/宝哥工具箱.exe`，76,928,440 字节（73.36 MiB）。SHA256：`E8826DD878C1CFF6B8AE10E2B6F04BC8930B72659087712A5A79C21BE01ABE84`。

两份 EXE 已验证存在、大小和 SHA256；本轮 UI 实测使用同源 Release 测试入口，未额外以管理员身份启动发布主程序。

## 边界

以上仅证明指定样例及链路，不承诺所有抖音/B 站视频或全部站点可下载。DRM、直播、失效链接、访问限制和没有可靠对应关系的 blob 保持拒绝/明确提示。直接 video 元素全屏时按钮可能无法叠放，应退出全屏使用原生工具栏或播放器按钮。

参考：[WebView2 frame 生命周期](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/frames)、[新窗口用户手势](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/winrt/microsoft_web_webview2_core/corewebview2newwindowrequestedeventargs)。
