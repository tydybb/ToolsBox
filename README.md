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
- `src/ToolsBox.Windows`：Windows IP Helper API 采集实现。
- `src/ToolsBox.App`：WPF 工具箱外壳和端口监控界面。
- `tests`：核心单元测试及 Windows 采集集成测试。

新增工具时应保留独立的领域/平台边界，并在 `ToolsBox.App` 中增加对应导航和视图。
