# ToolsBox

面向 Windows 的可扩展桌面工具箱，基于 .NET 8 和 WPF。首个工具是实时端口监控器。

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
- 文件夹检测自动包含其自身以及全部子目录和文件，不需要遍历大目录。
- 显示占用进程、PID、实际占用路径、句柄值、进程路径和权限状态。
- “安全结束所选进程”会结束占用进程，适合明确知道目标程序且可以接受未保存数据丢失的场景。
- “高级危险操作”可以只关闭所选文件句柄，不结束进程。
- 普通权限不足时会按需显示 Windows UAC 提示，不要求 ToolsBox 始终以管理员身份运行。

> 强制关闭句柄可能使目标程序崩溃、状态异常或损坏未保存数据。ToolsBox 会在执行前重新校验 PID、进程启动时间、句柄和路径，但无法消除目标程序自身无法处理句柄突然失效的风险。请优先正常关闭相关程序，其次使用“安全结束进程”，最后才使用强制关闭句柄。

## 环境要求

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（从源码运行或构建时）

## 运行

```powershell
dotnet run --project .\src\ToolsBox.App\ToolsBox.App.csproj
```

## 构建与测试

```powershell
dotnet test .\ToolsBox.slnx --configuration Release
dotnet build .\ToolsBox.slnx --configuration Release --no-restore
```

## 项目结构

- `src/ToolsBox.Core`：端口领域模型、筛选和刷新协调。
- `src/ToolsBox.Windows`：Windows IP Helper API、系统句柄扫描及解锁实现。
- `src/ToolsBox.App`：WPF 工具箱外壳、端口监控和文件解锁界面。
- `tests`：核心单元测试及 Windows 采集集成测试。

新增工具时应保留独立的领域/平台边界，并在 `ToolsBox.App` 中增加对应导航和视图。
