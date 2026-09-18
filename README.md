# 宝哥工具箱

面向 Windows 的可扩展桌面工具箱，基于 .NET 8 和 WPF。当前包含实时端口监控、文件解锁和软件网络流量监控工具。

## 端口监控

- 读取 TCP、UDP、IPv4、IPv6 端口。
- 显示本地/远程地址与端口、TCP 状态、PID 和进程名。
- 可按端口、PID、进程名、协议、状态或地址搜索。
- 自动刷新频率可选 1、2、5、10 秒，默认 2 秒。
- 支持暂停/继续自动刷新以及立即刷新。
- 刷新失败时保留上一次有效结果。

程序直接调用 Windows IP Helper API，不解析 `netstat` 文本，也不会结束任何进程。普通权限通常足以获取端口和 PID；个别受保护或已退出进程的名称可能显示为“无法访问”或“进程已退出”。

## 文件解锁

- 可输入完整路径、浏览选择文件/文件夹，或直接拖入单个文件/文件夹。
- 从普通资源管理器拖入管理员窗口时，使用 Windows 原生文件拖放接收路径；仅在“文件解锁”页面空闲时接收，放下后自动扫描。一次仅接收一个路径，拖入不会自动结束进程或关闭句柄。
- 文件夹检测自动包含其自身以及全部子目录和文件，不需要遍历大目录。
- 文件路径查询在无窗口辅助进程中隔离执行，单个句柄查询超过 250 毫秒会跳过并提示结果可能不完整；扫描最长 30 秒，结束或失败后恢复检测按钮。已以管理员身份启动时，“管理员扫描”直接复用当前权限。
- 显示占用进程、PID、实际占用路径、句柄值、进程路径和权限状态。
- “安全结束所选进程”会结束占用进程，适合明确知道目标程序且可以接受未保存数据丢失的场景。
- “高级危险操作”可以只关闭所选文件句柄，不结束进程。
- 宝哥工具箱启动时会统一请求管理员权限，文件解锁操作直接使用已获得的系统权限。

> 强制关闭句柄可能使目标程序崩溃、状态异常或损坏未保存数据。ToolsBox 会在执行前重新校验 PID、进程启动时间、句柄和路径，但无法消除目标程序自身无法处理句柄突然失效的风险。请优先正常关闭相关程序，其次使用“安全结束进程”，最后才使用强制关闭句柄。

## 网络流量

- 使用 Windows ETW 实时统计 TCP、UDP、IPv4 和 IPv6 的上传、下载流量。
- 默认按可执行文件路径汇总软件流量，可展开查看各个 PID 的实时速度和本次会话累计值。
- 采样只更新原位置的数据，不随瞬时网速重新排列；新软件追加到底部，保留选中行和展开详情。搜索或启用“仅显示有流量”时，列表仍会按筛选条件增减。
- 支持按软件名称、程序路径或 PID 搜索，也可以只显示当前有流量的软件。
- 可为能够读取完整路径的软件设置持久化上传限速，内置 128 KB/s、512 KB/s、1 MB/s 和 5 MB/s，也支持自定义值。
- 限速规则由 Windows QoS 提供，按可执行文件路径保存；软件或电脑重启后仍然生效，可在同一界面解除。

宝哥工具箱启动时会显示 Windows UAC 提示，必须通过管理员授权后才能进入主界面。文件解锁、ETW 网络监控和上传限速因此可以直接使用所需的系统权限；现有辅助进程仍负责隔离危险操作和网络采集生命周期。关闭工具箱时，网络辅助进程和 ETW 会话会一并退出。

> 当前版本只限制上传速度，不限制下载速度。完整的双向限速需要安装网络过滤驱动，已在架构中预留扩展接口，但本版本不安装驱动。系统进程或受保护进程可能无法读取程序路径，此类进程仍会显示流量，但不能设置限速。

## 环境要求

- Windows 10/11
- 使用发布版：安装 [.NET 8 Desktop Runtime（Windows x64）](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)。只装普通 .NET Runtime、ASP.NET Core Runtime 或其他架构版本可能无法运行 WPF 程序。
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（从源码运行或构建时）

## 运行

```powershell
dotnet run --project .\src\ToolsBox.App\ToolsBox.App.csproj
```

## 构建与测试

### 发布免安装单文件版（不带运行环境）

```powershell
dotnet publish .\src\ToolsBox.App\ToolsBox.App.csproj -c Release -p:PublishProfile=PortableWinX64
```

输出为 `bin\publish\win-x64\宝哥工具箱.exe`，目前约 12 MB。Visual Studio 中也可选择 `PortableWinX64` 发布配置。发布版保留启动时的管理员权限要求。

双击 EXE 后，.NET 原生启动器会先检查兼容的运行环境：

- 环境齐全：直接启动工具箱。
- 缺少 .NET 或所需的 Windows Desktop Runtime：显示官方提示（可能为英文），提供下载入口；用户确认后打开微软下载页面，不自动安装。
- 手动下载入口：[.NET 8 官方下载页](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)，选择 **.NET Desktop Runtime → Windows → x64**，安装完成后重新运行工具箱。运行发布版不需要安装 SDK。

该检查发生在 WPF/C# 代码执行前，不需要另加托管检测窗口。行为参考：[微软启动器说明](https://devblogs.microsoft.com/dotnet/dotnet-apphost-improvements/)。若电脑人为设置了 `DOTNET_DISABLE_GUI_ERRORS=1`，官方弹窗会被禁用；正常发布配置不会设置该变量。单文件依赖中的原生 DLL 会按 .NET 机制释放到用户临时目录，并非运行时完全不落盘。

### 自动化检查

```powershell
dotnet test .\ToolsBox.slnx --configuration Release
dotnet build .\ToolsBox.slnx --configuration Release --no-restore
```

网络辅助进程的实际运行验证需单独执行（Windows，可能显示 UAC 提示）：

```powershell
dotnet run --project .\tests\ToolsBox.NetworkSmoke --configuration Release -- "E:\work\ToolsBox\src\ToolsBox.App\bin\Release\net8.0-windows\宝哥工具箱.exe"
```

请将最后的路径替换为实际构建的程序路径。该验证检查辅助进程不打开主窗口、两轮 ETW 回环 UDP 流量采集与停止重启、读取 QoS 规则以及正常退出；不会新增或修改限速规则。日常单元测试不会自动触发 UAC。

可在管理员 PowerShell 中验证发布版的缺失环境错误路径：

```powershell
.\scripts\Test-MissingRuntime.ps1 -ExecutablePath .\bin\publish\win-x64\宝哥工具箱.exe
```

脚本只为测试子进程指定隔离的运行时目录，不卸载现有 .NET，不修改全局环境变量。为避免自动化被对话框阻塞，仅在测试子进程中禁用 GUI 提示并检查错误码、缺失框架名称和官方下载链接；这不代替干净 Windows 机器上的弹窗与下载按钮人工验收。

## 项目结构

- `src/ToolsBox.Core`：端口领域模型、筛选和刷新协调。
- `src/ToolsBox.Windows`：Windows IP Helper API、系统句柄扫描、ETW 网络采集、提权 IPC 和 QoS 管理实现。
- `src/ToolsBox.App`：WPF 工具箱外壳、端口监控、文件解锁和网络流量界面。
- `tests`：核心单元测试及 Windows 采集集成测试。

新增工具时应保留独立的领域/平台边界，并在 `ToolsBox.App` 中增加对应导航和视图。
