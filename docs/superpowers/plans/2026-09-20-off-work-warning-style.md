# 下班关机警告外观 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. 用户已确认蓝底白字、“即将关机”、去掉解释性小字、保留关闭按钮；仅视觉提示。沿用 main，不建分支、不提交或推送既有改动。

**Goal:** 将下班提醒改成系统风格的蓝色警告弹窗，保持只提醒一次及超时计时。

**Architecture:** 只修改 OffWorkReminderWindow.xaml 与现有 WPF 回归测试。继续复用 OnDismiss => Close()、MainWindow 非模态显示与现有 ViewModel；不增加计时器、关机命令、注销或进程终止行为。

**Tech Stack:** .NET 8、WPF、xUnit，不增加依赖。

## 1. 测试先行及界面修改

**Files:**
- Modify: `src/ToolsBox.App/Views/OffWorkReminderWindow.xaml`
- Test: `tests/ToolsBox.App.Tests/Startup/OffWorkReminderTests.cs`

- [x] 在现有实际 WPF 测试中增加以下断言，替换旧免责声明断言；执行测试确认因旧样式失败。

```csharp
Assert.Equal("即将关机", window.Title);
Assert.Equal(Color.FromRgb(0, 120, 215), Assert.IsType<SolidColorBrush>(window.Background).Color);
Assert.Null(window.FindName("ReminderDisclaimer"));
Assert.Equal("即将关机", Assert.IsType<TextBlock>(window.FindName("WarningTitle")).Text);
Assert.Equal("你已经下班，我要把你电脑关了", Assert.IsType<TextBlock>(window.FindName("ReminderMessage")).Text);
var dismiss = Assert.IsType<Button>(window.FindName("DismissButton"));
Assert.Equal("关闭", dismiss.Content);
```

```powershell
dotnet test tests/ToolsBox.App.Tests/ToolsBox.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~OffWorkReminderTests
```

- [x] 将 Window 改为 `Title="即将关机" Width="520" Height="350" Background="#0078D7" Foreground="White" FontFamily="Microsoft YaHei UI"`，保留 Topmost/ShowActivated/ShowInTaskbar/ResizeMode 和系统关闭按钮。
- [x] 使用 Margin=28,24 的 Grid：顶部大号白字 WarningTitle（28pt）及原句 ReminderMessage（18pt、换行）；中部白色细分隔线及 OverdueNotice（14pt）/OverdueTimer（32pt，原 ElapsedText 绑定）/预计下班 EndText；底部右侧 DismissButton，Content=关闭、Click=OnDismiss、110×36，蓝底白字白边框。删除 ReminderDisclaimer，不出现任何“即将执行真实操作”的额外倒计时。
- [x] 更新渲染视口至 520×320；核验标题、正文、关闭按钮均在内容区内。使用 RaiseEvent(Button.ClickEvent) 执行实际关闭回调，确认窗口 Closed、ViewModel 后续 Refresh 仍累加超时；不显示窗口，不访问真实用户状态。
- [x] 执行相关回归并查看 `off-work-reminder.png`，确认没有裁切、说明性小字已移除。

```powershell
dotnet test tests/ToolsBox.App.Tests/ToolsBox.App.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~OffWorkReminderTests|FullyQualifiedName~WorkCountdown'
```

## 2. 审查、打包和交付

- [x] 独立规范审查通过后做代码质量审查；只读核验从到点事件到窗口关闭没有新增系统操作。
- [x] 父代理重新运行相关测试、核验截图及 `git -c core.safecrlf=false diff --check`。
- [x] 依次发布现有两份 EXE 并核对哈希；如文件占用，不结束进程，报告原因。

```powershell
dotnet publish src/ToolsBox.App/ToolsBox.App.csproj -c Release --no-restore -p:PublishProfile=PortableWinX64 -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:PublishDir=E:/work/ToolsBox/bin/publish/no-archive-self-contained-win-x64/
dotnet publish src/ToolsBox.App/ToolsBox.App.csproj -c Release --no-restore -p:PublishProfile=PortableWinX64 -p:PublishDir=E:/work/ToolsBox/bin/publish/no-archive-win-x64/
```

- [x] 记录自动测试与离屏渲染边界，准备新的 EXE 交付链接；不宣称人工桌面/UAC 验证或真实关机验证。

## 验证与产物（2026-09-20）

此处为警告样式版本记录。后续下班按钮、状态持久化及发布状态见 [下班功能验证](2026-09-20-finish-work.md)。

- 测试先因旧标题“到下班时间了”与期望“即将关机”不符而失败，修改界面后相关 89 项测试通过；父代理独立重跑同一筛选，89 通过、0 失败、0 跳过。
- 查看 `tests/ToolsBox.App.Tests/bin/Release/net8.0-windows/off-work-reminder.png`：蓝底白字、标题与正文可见、无免责声明、关闭按钮未裁切。离屏布局中检查按钮 Visibility、尺寸和边界；不将未显示窗口的 IsVisible 当作真实桌面可见性。
- 调用按钮实际 Click 回调后窗口触发 Closed，ViewModel.ElapsedText 仍从 00:06:00 更新到 00:07:00；计时、事件及关闭逻辑三个 C# 文件 SHA256 与任务开始时一致，生产修改仅 XAML。
- README 同步说明视觉效果和不执行系统操作的边界。保留所有既有工作区改动，未提交或推送。
- 独立规范审查及随后代码质量审查均通过，无 Critical / Important / Minor 问题；核查范围限定在本次界面及对应测试。
- 两份 Release publish 均成功；未新增旧版副本，也未结束任何用户进程。

| 类型 | 路径 | 字节数 | SHA256 |
| --- | --- | ---: | --- |
| 内置 .NET | bin/publish/no-archive-self-contained-win-x64/宝哥工具箱.exe | 76528642 | 172F69D3EF1747AE48379FBE6ED60A1A6B439B9B55DF374759CA17E35EAD5EE8 |
| 不带运行环境 | bin/publish/no-archive-win-x64/宝哥工具箱.exe | 12313527 | 79AC6D4BE0D0B97EA1C0BDED22F6B7F9FD11FAF1B6D52490127901F491FF37D2 |

验证限于实际 WPF 控件的离屏渲染、绑定、事件与自动回归；未人工验证最终 EXE 的 UAC、真实最小化/休眠或高 DPI 桌面效果。没有也不应执行真实关机测试。
