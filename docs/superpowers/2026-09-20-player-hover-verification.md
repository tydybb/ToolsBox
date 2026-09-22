# 播放器下载按钮与抖音精选悬浮预览冲突修复

## 原因与修改

原悬浮按钮挂在 `document.documentElement` 下。鼠标从卡片移到按钮时，命中目标不再属于卡片，浏览器产生真实 `mouseleave`，网站随即停止悬浮预览。

按钮现挂在原 `video.parentElement` 中，不移动或包裹网站的视频，不修改网站容器样式；保持原有悬浮祖先链。采用绝对定位并测量局部原点和正向缩放，兼容容器滚动、平移、缩放和容器全屏。旋转、倾斜、镜像和 3D 容器保守隐藏按钮，列表入口仍可用。仅阻止按钮按下/点击事件冒泡，不伪造悬浮事件，也不强制播放。原生下载确认、来源和代次复核保持不变。

## 验证结果

- 旧实现：真实 WebView2 + CDP 鼠标从合成卡片移入按钮，出现 `Moving onto the download overlay ended the card's real hover preview: card`，确认回归测试能复现问题。证据：`bin/verification/hover-red-20260920/player-smoke-result.json`。
- 新实现：`--hover-continuity-only` 通过。验证卡片到按钮仍悬浮和播放、移出正常暂停、原生确认可打开和取消、没有任务启动、按钮点击及 pointerdown 不触发卡片、平移/缩放、滚动容器、容器全屏进出、特殊变换隐藏和恢复。证据：`bin/verification/hover-final-20260920/player-smoke-result.json`。
- 完整播放器集成 21 项通过，包括 iframe、旧确认失效、页面切换、实际 4 秒 H264/AAC MP4 下载、抖音元数据绑定、B 站当前单集绑定。证据：`bin/verification/hover-final-regression-20260920/player-smoke-result.json`。
- 按钮可用性专项通过：未绑定禁用，绑定后启用，播放器改变后失效。证据：`bin/verification/hover-final-availability-20260920/player-smoke-result.json`。
- 安全测试集合 682 项通过（Core 298、MediaDownloads 113、Windows 49、App 222），排除会操作文件句柄的 `CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess`。
- 构建 0 错误；探针构建中的 NU1900 表示漏洞数据源不可达，不等于已完成依赖漏洞审查。

## 真实抖音精选页观测

在隔离 WebView2 档案中访问 `/jingxuan`，动态定位可见封面并用 CDP 发送可信鼠标移动；普通登录提示出现后，通过核对文案及弹窗右上角 DOM 点击可关闭的 X，不输入凭据。

- 实际卡片 `waterfall-videoCardContainer / jingxuanVideoCard` 包含 React `onMouseEnter / onMouseLeave`；当前视频编号 `7685576602356387091`，来源 `blob`，完整媒体已绑定，下载按钮可用。
- 移入按钮后的四个快照，卡片 `:hover=true`，卡片 `mouseleave=0`；播放器元素、来源、编号和代次保持相同，`paused=false`，时间从 0.02 秒推进到 3.514626 秒。按钮实际父节点为 `XG-VIDEO-CONTAINER`，仍在该卡片内部。
- video 元素自身产生真实 `mouseleave`，但处理预览生命周期的卡片没有离开；这是本次真实页面与合成卡片假设相符的证据。
- 正常移到卡片外，卡片收到一次可信 `mouseleave`，网站移除预览播放器，原生可见播放器列表清空。没有人为维持播放。
- 测试未点击下载按钮，下载任务增量为 0；按钮点击/取消确认另由合成 WebView2 测试覆盖。
- 日志及八张前后截图：`bin/verification/hover-live-cards-20260920/`。首次探针因普通登录提示、随后因封面不在链接中未取得目标；改进测试定位后才获得以上观测，未以定位失败冒充成功。

## 边界

合成卡片测试与真实站点观测分开记录，不据此保证所有网站或抖音所有版本。网站若直接对 video 自身绑定离开处理，或在祖先捕获阶段处理所有按下事件，仍可能需要专门适配。直接 video 元素全屏仍不放按钮。本轮不登录、不读取用户现有浏览器档案，也不自动进行真实站点下载。

## 发布包

两种打包脚本成功执行，均生成新的独立目录，不覆盖旧文件。通过 `--web-resources invalid invalid` 做无 UI 启动校验，两者均按预期退出 1；这不是已对打包后所有 UI 做人工验收的声明。

- 不含 .NET：`bin/publish/framework-dependent-win-x64/20260920-170014-527-c472ec0a337244eeba708a4f25614a18/宝哥工具箱.exe`，13.13 MiB，SHA256 `782FC56C0D5A32FB28697F8731B7FFC889EC982A6777E556A216CB13CB6611C7`。
- 内置 .NET：`bin/publish/self-contained-win-x64/20260920-170040-716-e4de28cb1bb543f890aaf90b9c3c84b1/宝哥工具箱.exe`，73.37 MiB，SHA256 `6B91DC049A5363E2702C988359BDB6585604C4920E06524568C891E3F3190689`。
