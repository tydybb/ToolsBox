# 钉钉打卡提醒实施计划

> **For agentic workers:** Use superpowers:subagent-driven-development or superpowers:executing-plans to implement task-by-task. Preserve existing main work; no commit/push requested.

**Goal:** 可选本地打卡读取、工作日早间提醒、普通权限登录自启、打卡时间导入下班倒计时。

**Architecture:** Core 的本地读取器只返回最小打卡结果；App 的提醒协调器负责日历/活动事件/五分钟节流/09:25末次/每日确认；独立普通权限提醒宿主通过共享最小状态文件向完整工具箱交付打卡结果，完整工具箱负责倒计时覆盖确认。不开系统服务、不提权自启、不改钉钉原库。

**Tech Stack:** .NET 8、WPF、Windows SessionSwitch/PowerModeChanged、HKCU Run、JSON 原子保存、xUnit。

## 已批准行为

- 默认关闭，勾选前明确本地数据读取和登录自启，关闭即撤销自启。
- 07:30 起，仅工作日/调休；手动当天覆盖可用。登录、唤醒、解锁检查，活动时五分钟兜底；09:25最后提醒，之后到09:30只允许补读不再弹提醒。锁屏期间不弹，错过最后窗口不补弹。
- 置顶但不阻塞电脑的提醒框，“我已打卡”当天停止提醒，手动确认不生成打卡时间；次日自动失效。
- 今日上班成功记录自动填入和启动计时；已有计时或未保存输入存在冲突先询问，拒绝同一记录后不反复询问。已下班的任务不自动恢复。
- 自动定位当前用户目录与重定向；多账号不猜，手动选择数据目录。读不到显示“尚未确认”，不是断言未打卡。仅辅助识别，不代替企业考勤记录。
- 默认使用合成数据/隔离测试文件，不注册真实自启。用户后续明确允许仅只读验证本机今日数据，因此增加一次实际兼容性检查；不输出聊天正文、账号标识、密钥，不修改原库。

## Task 1: 本地读取器与验证

Files: `src/ToolsBox.Core/Attendance/*.cs`, `tests/ToolsBox.Core.Tests/Attendance/*.cs`。

- [ ] 为同日成功/下班/旧日期/歧义、多账号路径和读取失败写测试，先运行失败。
- [ ] 补齐现有不完整解析草稿；保留 `IAttendanceChecker.Check(DateTime)` 和 `AttendanceStatus` 接口。约束输入大小、读操作、临时明文生命周期，异常转未知，不把普通聊天的模糊“打卡”当成功。
- [ ] 用合成库或消息测试，不接触用户真实消息。命令 `dotnet test tests/ToolsBox.Core.Tests --filter FullyQualifiedName~Attendance`。

## Task 2: 规则、状态及倒计时导入

Files: `src/ToolsBox.App/Attendance/AttendanceSettings.cs`, `AttendanceCoordinator.cs`, `src/ToolsBox.App/WorkCountdown/WorkCountdownViewModel.cs`, `tests/ToolsBox.App.Tests/Attendance/`。

- [ ] 先测试时间边界、09:25强制末次、锁屏、跨日、手动确认和失败状态；导入测试既有任务冲突、早于07:30和记录日期。
- [ ] 实现 `AttendancePolicy.ShouldCheck(DateTime,bool,bool)` 与 `ShouldRemind(DateTime)`，只记录本日确认、末次提醒、最小打卡时间；任何异步结果检查日期和是否仍启用。
- [ ] 导入使用 `WorkCountdownViewModel.ImportClockIn(DateTime, Func<bool>)`，拒绝旧日期/未来，已有计划冲突调用确认委托；不覆盖未保存加班设置。
- [ ] 运行 App 的 Attendance 测试并验证此前倒计时测试。

## Task 3: 自启宿主和 UI

Files: `src/ToolsBox.App/Attendance/AttendanceStartup.cs`, `AttendanceHost.cs`, `AttendancePanel.cs`, `AttendanceReminderWindow.cs`, `App.xaml.cs`, `MainWindow.xaml.cs`, `Views/WorkCountdownView.xaml*`。

- [ ] 用注入注册表替身测试命令引号、只移除本功能Run项；不真实写注册表。
- [ ] `--attendance-agent` 在管理员启动之前分流，只运行本功能，按用户/会话单实例，使用托盘和可配置设置窗口；HKCU Run 指向固定当前EXE并保留dotnet入口参数。关闭功能时即刻停止读取并清理提醒，不修改其他工具自启。
- [ ] UI勾选需确认；添加数据路径自动/浏览、当天工作日覆盖、状态、立即检查、我已打卡；数据路径/启用配置仅在明确操作时保存。
- [ ] 主窗口只消费共享结果，提醒宿主不直接覆盖倒计时文件。启动时/运行中导入均需冲突确认，隐藏主窗口时仍显示确认；进程退出撤销活动事件订阅。

## Task 4: 验收与交付

- [ ] 规格审查、代码审查、合成记录 UI 检查，修复发现的问题。
- [ ] `dotnet test ToolsBox.slnx -c Release --filter FullyQualifiedName!=ToolsBox.App.Tests.Startup.SystemHandleScannerTests.CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess`。
- [ ] 更新 README 和验收记录，分别构建两种 EXE；不终止用户进程、不覆盖旧 EXE。明确真实钉钉版本/登录自启/锁屏未验证的边界。

## 2026-09-22 实施验收

- Tasks 1–3 已实现。实际规则入口为 `TryBeginCheck` / `TryRemind`；主窗口不存在时，普通权限宿主拥有后台倒计时，主窗口存在时让出所有权。
- 安全回归 731 项通过：Core 324、MediaDownloads 113、Windows 49、App 245。明确排除真实关闭文件句柄用例；未操作用户进程。
- 已检查倒计时界面渲染图；修复跨日待保存输入、异步读取跨越末次提醒、确认窗口期间设置变化等问题。
- 经用户授权，根代理实际只读复核到当日 07:50 上班记录；WAL 校验及内存数据库 quick_check 通过。未导出聊天明文、账号或密钥，未修改原库。
- 未实际注册登录自启，也未验证重启、锁屏/休眠生命周期或其他电脑的钉钉版本。NuGet 漏洞源曾不可达（NU1900），测试成功不代表依赖安全审计成功。
- README 已更新；两种包使用原有入口分别发布到全新时间戳目录，不覆盖旧包、不提交或推送。
