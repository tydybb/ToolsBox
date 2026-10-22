# 参与贡献

欢迎通过 Issue 提供可复现的问题、提出改进建议，或提交 Pull Request。较大的功能和行为变更建议先说明使用场景与方案。

## 开发环境

- Windows 10/11，.NET 8 SDK 或兼容的更新 SDK；仅安装 Runtime 无法构建。
- 主程序是 `net8.0-windows` 的 WPF 应用。首次还原依赖需要访问 NuGet。
- 仓库提供 `ToolsBox.slnx`，较旧 SDK 可能不识别该格式；以下命令直接使用项目文件。

从仓库根目录的 PowerShell 构建：

```powershell
dotnet build .\src\ToolsBox.App\ToolsBox.App.csproj --configuration Release
```

运行主界面会请求 Windows 管理员授权，取消授权会退出：

```powershell
dotnet run --project .\src\ToolsBox.App\ToolsBox.App.csproj
```

构建和以下日常测试不需要以管理员身份运行。网页、媒体组件和打卡读取辅助进程有各自的普通权限边界，修改时请保留这些边界。

## 日常验证

按项目运行这四组测试，App 测试必须保留筛选条件：

```powershell
dotnet test .\tests\ToolsBox.Core.Tests\ToolsBox.Core.Tests.csproj --configuration Release
dotnet test .\tests\ToolsBox.Windows.Tests\ToolsBox.Windows.Tests.csproj --configuration Release
dotnet test .\tests\ToolsBox.MediaDownloads.Tests\ToolsBox.MediaDownloads.Tests.csproj --configuration Release
dotnet test .\tests\ToolsBox.App.Tests\ToolsBox.App.Tests.csproj --configuration Release --filter 'FullyQualifiedName!~SystemHandleScannerTests.CloseHandleAsync'
```

`SystemHandleScannerTests.CloseHandleAsync...` 会实际关闭测试辅助进程中的文件句柄，只能在理解影响的隔离环境中单独人工运行。不要把不带筛选的全量测试作为默认检查。

需要管理员权限的真实进程操作、ETW 网络采集、QoS 限速，以及 `tests` 下的独立 Smoke/Probe 程序，应先阅读其入口和参数，再按改动范围人工执行。使用自建测试进程、临时文件与合成数据，不对正在工作的进程或真实业务数据进行破坏性验证。

自动测试和 CI 通过只证明覆盖到的检查通过，不能代替实际桌面布局、UAC、托盘与提醒、休眠恢复、真实钉钉版本、网站登录、播放器及 DRM 边界的人工验收。提交时说明实际验证了什么，以及哪些检查尚未运行。

## 提交修改

1. 外部贡献者可 Fork 仓库，从自己的分支提交面向 `main` 的 Pull Request。
2. 保持改动集中；领域规则优先放在 `ToolsBox.Core`，Windows 系统能力放在 `ToolsBox.Windows`，媒体组件放在 `ToolsBox.MediaDownloads`，WPF 界面与交互放在 `ToolsBox.App`。
3. 对行为变化增加或更新有意义的回归测试，遵循周围代码风格。不要提交生成的 `bin`、`obj`、发布包或本机配置。
4. PR 中说明问题、最终行为、测试结果和必要的截图；界面修改请说明 Windows 显示缩放与主题。提交前运行 `git diff --check`。

代码贡献遵循仓库 [LICENSE](LICENSE) 指定的 GPL-3.0-only。新增素材与依赖请说明来源和可再分发许可；没有相应许可的素材不得加入公开发布包。请只提交有权分享的代码、素材和数据。

不要在 Issue、PR、日志或测试附件中包含真实钉钉数据库、聊天内容、账号信息、Cookie、访问令牌、密码或其他凭据；使用脱敏日志与合成样例。疑似漏洞请按 [安全报告说明](SECURITY.md) 私下反馈。
