# 下班按钮与结束状态 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. 用户已选择 A：按工作日正常下班时间判断早退，计划加班未完成不算早退。沿用 main，保留既有改动，不提交、不推送、不创建分支。

**Goal:** 点击“下班”结束本次计时，弹一次结果提示，并在倒计时面板保留实际下班时间与相同结果。

**Architecture:** CountdownState 增加兼容旧 JSON 的可选 FinishedAt；WorkCountdownViewModel 管理结束状态及 WorkFinished 事件。MainWindow 负责一次性结果提示及关闭旧到点警告，WPF View 绑定命令和结果。保存失败仍结束本次计时并告警；同日恢复结束状态不再次提醒。

**Tech Stack:** .NET 8、WPF、xUnit，沿用现有时钟和状态存储注入，不新增依赖。

## 确定的规则与接口

- 工作日完成时间早于 `Schedule.NormalEnd` 才是早退；迟到继续使用既有固定 18:30 规则。提示“你早退了，距离正常下班还差 XX 分钟。”，不足一分钟向上取整。
- 正常或超时下班显示“已下班，抓紧回家吧！”。若计划加班没有完成，详情单独提示“计划加班未完成”；不是早退时附“不计为早退”。周末/节假日只有此加班提示，不标记早退。
- 取按钮点击瞬间的时钟，不先调用会触发到点警告的 Refresh。只采用已生效任务，不采用未应用的输入草稿。
- 冻结本次计时值，停止 DispatcherTimer，设置 IsOverdue=false，阻止后续到点提醒，关闭已经打开的到点警告。结果弹窗关闭不清空面板结果。未开始、尚未到上班时刻、已结束、已释放时不可下班；方法内部同样防重复和时钟回拨。
- 成功重新开始恢复计时并清除结束状态；非法重新计算保持结束状态。重置清空结束状态。旧日记录继续不恢复，同日已结束记录恢复后保持停止、不补弹提醒。

VM 接口：`FinishCommand`、`WorkFinished`（EventHandler）、`IsFinished`、`FinishedAt`（DateTime?）、`IsEarlyDeparture`、`HasIncompleteOvertime`、`FinishMessage`、`FinishDetails`（实际下班时间及未完成加班提示）、`FinishedTimerLabel`。StatusText 在结束后使用 FinishMessage，TimerText 冻结为距最终预计下班的剩余时长或超出时长，FinishedTimerLabel 明确“计时已停止”。

## 1. 状态逻辑与存储

Files：`src/ToolsBox.App/WorkCountdown/WorkCountdownViewModel.cs`、`CountdownStateStore.cs`；测试 `tests/ToolsBox.App.Tests/WorkCountdown/WorkCountdownFinishTests.cs`、`CountdownStateStoreTests.cs`。

- [x] 先写失败测试，覆盖早退/准点/晚下班、加班未完成不早退、休息日、迟到、时钟未刷新直接点击、草稿、未开始/未来上班/重复/释放、保存失败、恢复/重新开始/重置、真正 DispatcherTimer 停止。
- [x] 使用可选字段 `DateTime? FinishedAt = null` 保持四参数构造及旧 JSON 可读；恢复时拒绝完成时间早于任务上班时间的损坏记录。
- [x] 实现 Finish：先校验任务及当前时间，再保存完成时刻、停止计时和旧警告，最后发送一次 WorkFinished。显示时间采用 `FinishedAt ?? _currentTime`，Refresh 不改变完成时刻。
- [x] 新旧 JSON、结束时刻往返、失败保存和同日恢复自动测试通过。

核心验收示例（注入可变 now 与内存存储，不访问用户状态）：

```csharp
vm.StartTimeText = "0930";
vm.OvertimeText = "2";
vm.StartCommand.Execute(null);
now = new DateTime(2026, 9, 18, 18, 30, 0);
vm.FinishCommand.Execute(null);
Assert.True(vm.IsFinished);
Assert.False(vm.IsEarlyDeparture);
Assert.True(vm.HasIncompleteOvertime);
Assert.Equal("已下班，抓紧回家吧！", vm.FinishMessage);
string frozen = vm.TimerText;
now = now.AddHours(5);
vm.Refresh();
Assert.Equal(frozen, vm.TimerText);
Assert.False(vm.IsOverdue);
```

## 2. 按钮、面板和提示联动

Files：`src/ToolsBox.App/Views/WorkCountdownView.xaml`、`src/ToolsBox.App/MainWindow.xaml.cs`；测试 `tests/ToolsBox.App.Tests/Startup/FinishWorkIntegrationTests.cs`。

- [x] 先写实际 WPF 测试：按钮 FinishWorkButton 存在并绑定 FinishCommand，结束后禁用，StatusText 变为完成提示，FinishDetailsText 显示实际时间及加班提示，OverdueNotice 隐藏，冻结时钟不再变动。
- [x] MainWindow 保留现有两参数构造，再增加三参数注入结果提示 Action 的构造；订阅 WorkFinished，关闭主窗口时解绑。默认结果提示为有主窗口的 MessageBox，正常 Information、早退 Warning；仅点击事件触发，不在恢复时触发。
- [x] 面板添加绿色“下班”按钮，结束后的状态标题用绿色，早退用橙色；显示 FinishDetails 和 FinishedTimerLabel，保留原计划数据。按钮容器使用 WrapPanel，窄窗口不挤压。
- [x] 测试使用注入提示回调，不弹真实 MessageBox、不运行真实系统操作。核验一次结果事件、已打开旧警告关闭、主窗口关闭解绑；查看正常下班/早退 WPF 离屏截图。

```powershell
dotnet test tests/ToolsBox.App.Tests/ToolsBox.App.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~FinishWorkIntegrationTests|FullyQualifiedName~WorkCountdown|FullyQualifiedName~CountdownStateStoreTests|FullyQualifiedName~OffWorkReminderTests'
```

## 3. 验证与发布

- [x] 独立规范及代码质量审查通过，保留真实桌面/UAC 未验证说明。
- [x] 运行全解决方案安全筛选回归与 diff 检查；排除旧的真实句柄关闭测试，不执行任何真实用户进程结束、句柄关闭或关机。

```powershell
dotnet test ToolsBox.slnx -c Release --no-restore --filter 'FullyQualifiedName!=ToolsBox.App.Tests.Startup.SystemHandleScannerTests.CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess'
git -c core.safecrlf=false diff --check
```

- [x] 更新 README、发布并核对现有两份 EXE；如果目标正在运行，不结束进程来覆盖。

```powershell
dotnet publish src/ToolsBox.App/ToolsBox.App.csproj -c Release --no-restore -p:PublishProfile=PortableWinX64 -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:PublishDir=E:/work/ToolsBox/bin/publish/no-archive-self-contained-win-x64/
dotnet publish src/ToolsBox.App/ToolsBox.App.csproj -c Release --no-restore -p:PublishProfile=PortableWinX64 -p:PublishDir=E:/work/ToolsBox/bin/publish/no-archive-win-x64/
```

## 验证记录

- RED：状态测试因缺少 FinishCommand 运行时断言失败；4 项实际 WPF 集成测试因缺少新提示入口失败。补充禁用文字颜色回归时观察到期望灰色而实际白色的 3 项失败，再修复样式。
- 最终安全筛选全回归：**308 通过，0 失败**，其中 Core 109、Windows 49、App 150。主动排除上述 1 项真实句柄关闭测试，不是框架跳过项。
- 实际文件保存 FinishedAt，再通过 JsonCountdownStateStore 创建 ViewModel，核验结束状态与冻结时长；另覆盖旧 JSON、损坏记录、内存存储失败等边界。
- 控件绑定、一次结果事件、禁用按钮、已有到点窗口关闭、事件解绑及关闭后冻结均有自动测试。DispatcherTimer 实例验证停止、恢复后不启动、有效开始和重置恢复；时间推进使用注入时钟。
- 查看 `tests/ToolsBox.App.Tests/bin/Release/net8.0-windows/countdown-finished-early.png` 与 `countdown-finished-normal.png`。早退不再显示“正常出勤”；停止后隐藏运行中的趣味文案，保留计划时长和时段；禁用按钮文字清晰。
- 独立规范审查、增量规范复核、最终代码质量审查通过，无待修复问题。保留用户所有既有未提交改动；未提交、未推送。
- 未人工验证最终 EXE 的 UAC、高 DPI、真实休眠和系统 MessageBox 操作；离屏 WPF 与注入提示回调测试不等同真实桌面人工验收。

## 发布状态

两份 Release 发布最终均成功，已覆盖原路径，没有额外生成旧版副本。

| 类型 | 路径 | 字节数 | SHA256 |
| --- | --- | ---: | --- |
| 内置 .NET | bin/publish/no-archive-self-contained-win-x64/宝哥工具箱.exe | 76533762 | 559D897A0FFDD1DEA288FF47C8372741EBD27FF09770B0F6887564C289810574 |
| 不带运行环境 | bin/publish/no-archive-win-x64/宝哥工具箱.exe | 12317623 | E15F673BCB7881ECA339BE7C771CE88AF9D0D623ED296FAEFB7494DC5DDBC556 |

内置 .NET 版首次覆盖被拒绝访问，只读查询发现工具箱进程正在运行。用户确认已正常退出后重新发布成功；未结束任何用户进程或修改文件权限。
