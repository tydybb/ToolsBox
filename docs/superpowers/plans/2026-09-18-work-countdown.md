# 下班倒计时 Implementation Plan

> 使用 superpowers:subagent-driven-development 执行；用户已明确实施，沿用 main，不另建分支或工作树。

**Goal:** 接入遵循已确认出勤规则的本地下班倒计时。

**Architecture:** Core 提供无 UI 依赖的日历、日期分类和时间计算。App VM 维护固定日期任务和持久化，单一 DispatcherTimer 按当前系统时间更新，MainViewModel 持有并释放。

**Tech Stack:** .NET 8、WPF、System.Text.Json、现有 xUnit；不新增 NuGet 包。

## 1. 计算与日历

- [x] 在 tests/ToolsBox.Core.Tests/WorkCountdown 新增规则与日历测试，先运行观察缺失 API，再实现 src/ToolsBox.Core/WorkCountdown。
- API：`DayKind { Workday, RestDay, Unknown }`，`ChinaWorkCalendar.GetDay(DateOnly)` 返回 `DayClassification(Kind, Label)`。
- API：`CountdownCalculator.Calculate(DateOnly, TimeOnly, DayKind, int)` 返回 `WorkSchedule(Start, NormalEnd?, End, IsLate, ExcludedBreaks)`；非法输入抛 ArgumentException。
- 关键断言：工作日 09:30+2 =>21:00；休息日09:30+4=>14:30；工作日09:31=>迟到、18:30；12:00午休开始不计工时；未知年份自动停止。
- [x] `dotnet test tests/ToolsBox.Core.Tests -c Release`，核验所有规则通过。

## 2. 状态与界面

- [x] tests/ToolsBox.App.Tests/WorkCountdown 新增可控时钟、存储、输入/恢复和 WPF 绑定测试。
- [x] src/ToolsBox.App/WorkCountdown：CountdownStateStore.cs（JSON 原子保存/错误提示）、WorkCountdownViewModel.cs（开始/重置/恢复/时钟）。
- [x] Views/WorkCountdownView.xaml 与 code-behind；沿用工具箱颜色和控件样式，大号倒计时、规则说明、错误提示。
- [x] MainViewModel、MainWindow.xaml 增加工具、导航选中状态、资源模板、释放逻辑。非文件页面不接收文件拖放。
- [x] 修改 Startup/WindowStartupAndNavigationTests，覆盖四个工具顺序与新页面名称。

## 3. 验证与交付

- [x] 独立规范/质量审查并处理实际问题，不动无关代码。
- [x] `dotnet test ToolsBox.slnx -c Release` 与 `git diff --check`。
- [x] README 增加计时规则、日历年份、存储、使用方式及限制。
- [x] 顺序发布 PortableWinX64，分别 `SelfContained=true`（压缩）和 false，更新现有 no-archive 两个发布目录，不制造旧版 EXE 副本。
- [x] 记录真实结果、人工未验证边界，交付 EXE 链接。不擅自推送此前尚未提交的变更。
