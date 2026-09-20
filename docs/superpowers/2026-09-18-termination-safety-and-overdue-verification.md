# 进程终止安全修复与下班超时提醒验证记录

日期：2026-09-18。沿用本地 main，未提交或推送；保留此前全部无关工作区改动。

本记录保留 9 月 18 日结果；后续蓝色警告样式、免责声明移除与最新 EXE 哈希见 [9 月 20 日样式更新验证](plans/2026-09-20-off-work-warning-style.md)。

## 已实现

- 强制结束仅操作确认清单中的单个进程，不递归结束子进程。确认前展示可滚动的名称、PID、程序路径，默认取消，明确确认数据丢失风险后才能执行。
- 增加资源管理器和常见桌面进程保护；受保护或身份不可信的任一选中项会阻止整批操作。保护名单不代表能识别所有关键进程。
- 先校验全部选中记录，再按 PID 去重；冲突身份要求重新扫描。确认和执行使用同一只读快照，期间锁定路径及扫描。
- 在读取身份前固定进程句柄，并持续持有至校验、结束及等待完成，避免 PID 复用导致校验与执行目标不一致。普通及提权流程共用相同保护。
- 到最终预计下班时间（包含已设置加班）后，面板显示“你已无偿加班”并按秒累计超出时间，支持超过 24 小时。
- 每个活动任务到点弹窗一次；弹窗非模态、不抢焦点，关闭弹窗不会停止计时。切换工具页面仍能提醒。重复开始相同任务不会重复提醒；重置或建立新任务重新计数。提醒状态不跨进程持久化，重启恢复已到点任务会在本次运行提醒一次。
- 随后按用户要求将弹窗主文案替换为“你已经下班，我要把你电脑关了”，补充“仅为提醒，不会真的关机。”；仅修改 XAML 文案，未增加系统关机、注销或结束进程行为，无偿加班计时保持不变。

## 自动验证与审查

最终执行：

```powershell
dotnet test ToolsBox.slnx -c Release --no-restore --filter 'FullyQualifiedName!=ToolsBox.App.Tests.Startup.SystemHandleScannerTests.CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess'
git -c core.safecrlf=false diff --check
```

- 287 项测试通过：Core 109、Windows 49、App 129；0 失败。
- 通过筛选主动排除 1 项真实句柄关闭测试，不是测试框架报告的跳过项。进程终止测试仅调用 Fake 边界；新增适配器测试只读取测试进程自己的句柄并释放自身句柄，不结束进程。
- 新增行为均先通过失败测试确认缺口，再修复至通过，包括非递归结束、保护清单、快照/取消/重入、PID 身份冲突、句柄固定、到点和正计时、一次性事件及窗口解绑。
- 独立规范和代码质量审查通过；修复审查发现的保护记录被过早去重、PID 复用竞态问题。
- WPF 离屏渲染/绑定检查覆盖完整确认清单、风险门槛和提醒窗口。截图检查发现名称/PID 列过窄，增加最小列宽与回归断言后重新运行全部 287 项测试通过。
- 已检查截图：`tests/ToolsBox.App.Tests/bin/Release/net8.0-windows/process-termination-confirmation.png`、`tests/ToolsBox.App.Tests/bin/Release/net8.0-windows/off-work-reminder.png`。
- 文案替换单独遵循先失败后通过：新增弹窗文字断言先失败；替换后执行 `dotnet test tests/ToolsBox.App.Tests/ToolsBox.App.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~OffWorkReminderTests|FullyQualifiedName~WorkCountdown'`，89 项相关测试全部通过，并重新检查提醒窗口截图。只读独立审查确认提醒事件及关闭窗口路径不会关机或注销。上方 287 项是文案替换前的全解决方案安全筛选结果。

## 发布产物

两次 Release publish 均成功，覆盖既有输出路径，未新增旧版 EXE 副本。

| 类型 | 路径 | 字节数 | SHA256 |
| --- | --- | ---: | --- |
| 内置 .NET，win-x64 | bin/publish/no-archive-self-contained-win-x64/宝哥工具箱.exe | 76528642 | 7A7AA74810640B3C45DE0C90B050B209D42288A08B03BEA76617CCD6123094ED |
| 不内置 .NET，win-x64 | bin/publish/no-archive-win-x64/宝哥工具箱.exe | 12313527 | 6C5A84444658C7796BF9330F3E11FBB08029563E186C073AB3EC62B597C7B79F |

文案替换后两份 EXE 已重新发布。不内置运行环境版第一次覆盖被拒绝访问，同时发现工具箱进程在运行；只读复查时进程已退出，再次发布成功，未结束任何用户进程。

构建命令：

```powershell
dotnet publish src/ToolsBox.App/ToolsBox.App.csproj -c Release --no-restore -p:PublishProfile=PortableWinX64 -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:PublishDir=E:/work/ToolsBox/bin/publish/no-archive-self-contained-win-x64/
dotnet publish src/ToolsBox.App/ToolsBox.App.csproj -c Release --no-restore -p:PublishProfile=PortableWinX64 -p:PublishDir=E:/work/ToolsBox/bin/publish/no-archive-win-x64/
```

## 验证边界与使用提醒

- 未结束任何真实用户进程，也未用真实文件句柄关闭来证明强制操作效果；Fake 回归和身份读取测试不等同实际强杀验收。
- 未人工验证最终 EXE 的 UAC、最小化时弹窗及真实休眠恢复；离屏 WPF 渲染、绑定和时钟注入测试不等同上述桌面交互。
- 使用前请关闭旧版并启动新 EXE。强制结束仍可能丢失未保存数据；软件自身关联窗口或服务也可能因主程序退出而退出，不能承诺零连带影响。
