# 第三方许可清单

本清单核对日期为 2026-10-22，来源是四个生产项目文件、App 的 `obj/project.assets.json` 中 `net8.0-windows/win-x64` 还原图、NuGet 包内 `.nuspec` / 许可文件及其构建导入文件。完整机器可读记录见 [dependency-inventory.json](dependency-inventory.json)。

本次发布还原快照记录 27 个第三方 NuGet 包，包含构建工具、元包与框架占位资源；其中 ILLink 是 SDK 在发布还原中自动加入的构建工具，普通 build 还原后可能不在图中。表中的“运行时程序集候选”表示 NuGet 选中的资源；最终发布会处理共享框架与包版本冲突，因此不能把还原图的每个 DLL 都视为独立捆绑文件。WebView2 托管引用和 TraceEvent 辅助文件还会由包的 targets / props 加入，已单列在机器可读记录中。

## 包与许可

| 包 / 版本 | 许可原文 | 用途 |
| --- | --- | --- |
| [Microsoft.Data.Sqlite/8.0.1](https://www.nuget.org/packages/Microsoft.Data.Sqlite/8.0.1) | [MIT](DOTNET-LICENSE-MIT.txt) | 元包 / 无独立运行时 DLL |
| [Microsoft.Data.Sqlite.Core/8.0.1](https://www.nuget.org/packages/Microsoft.Data.Sqlite.Core/8.0.1) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [Microsoft.Diagnostics.NETCore.Client/0.2.510501](https://www.nuget.org/packages/Microsoft.Diagnostics.NETCore.Client/0.2.510501) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [Microsoft.Diagnostics.Tracing.TraceEvent/3.1.21](https://www.nuget.org/packages/Microsoft.Diagnostics.Tracing.TraceEvent/3.1.21) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [Microsoft.Extensions.DependencyInjection/6.0.0](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/6.0.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [Microsoft.Extensions.DependencyInjection.Abstractions/6.0.0](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/6.0.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [Microsoft.Extensions.Logging/6.0.0](https://www.nuget.org/packages/Microsoft.Extensions.Logging/6.0.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [Microsoft.Extensions.Logging.Abstractions/6.0.0](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/6.0.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [Microsoft.Extensions.Options/6.0.0](https://www.nuget.org/packages/Microsoft.Extensions.Options/6.0.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [Microsoft.Extensions.Primitives/6.0.0](https://www.nuget.org/packages/Microsoft.Extensions.Primitives/6.0.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [Microsoft.NET.ILLink.Tasks/8.0.30](https://www.nuget.org/packages/Microsoft.NET.ILLink.Tasks/8.0.30) | [MIT](DOTNET-LICENSE-MIT.txt) | 构建工具 |
| [Microsoft.NETCore.Platforms/5.0.0](https://www.nuget.org/packages/Microsoft.NETCore.Platforms/5.0.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 元包 / 无独立运行时 DLL |
| [Microsoft.Web.WebView2/1.0.4191.47](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4191.47) | [Microsoft BSD 三条款原文](WEBVIEW2-1.0.4191.47-LICENSE.txt) | 托管 SDK + 原生 Loader |
| [Microsoft.Win32.Registry/5.0.0](https://www.nuget.org/packages/Microsoft.Win32.Registry/5.0.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [SQLitePCLRaw.bundle_e_sqlite3/2.1.6](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.6) | [Apache-2.0](SQLITEPCLRAW-APACHE-2.0.txt) | 运行时程序集候选 |
| [SQLitePCLRaw.core/2.1.6](https://www.nuget.org/packages/SQLitePCLRaw.core/2.1.6) | [Apache-2.0](SQLITEPCLRAW-APACHE-2.0.txt) | 运行时程序集候选 |
| [SQLitePCLRaw.lib.e_sqlite3/2.1.13](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/2.1.13) | [Apache-2.0](SQLITEPCLRAW-APACHE-2.0.txt) | 原生 SQLite 库 |
| [SQLitePCLRaw.provider.e_sqlite3/2.1.6](https://www.nuget.org/packages/SQLitePCLRaw.provider.e_sqlite3/2.1.6) | [Apache-2.0](SQLITEPCLRAW-APACHE-2.0.txt) | 运行时程序集候选 |
| [System.Collections.Immutable/8.0.0](https://www.nuget.org/packages/System.Collections.Immutable/8.0.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [System.Diagnostics.DiagnosticSource/6.0.0](https://www.nuget.org/packages/System.Diagnostics.DiagnosticSource/6.0.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [System.Memory/4.5.3](https://www.nuget.org/packages/System.Memory/4.5.3) | [MIT](DOTNET-LICENSE-MIT.txt) | 当前目标选中框架占位资源 |
| [System.Reflection.Metadata/8.0.0](https://www.nuget.org/packages/System.Reflection.Metadata/8.0.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [System.Reflection.TypeExtensions/4.7.0](https://www.nuget.org/packages/System.Reflection.TypeExtensions/4.7.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 当前目标选中框架占位资源 |
| [System.Runtime.CompilerServices.Unsafe/6.0.0](https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe/6.0.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [System.Security.AccessControl/5.0.0](https://www.nuget.org/packages/System.Security.AccessControl/5.0.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [System.Security.Principal.Windows/5.0.0](https://www.nuget.org/packages/System.Security.Principal.Windows/5.0.0) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |
| [System.Text.Json/8.0.5](https://www.nuget.org/packages/System.Text.Json/8.0.5) | [MIT](DOTNET-LICENSE-MIT.txt) | 运行时程序集候选 |

Microsoft 包的 NuGet 元数据版权声明为 `© Microsoft Corporation. All rights reserved.`；相应上游 MIT 文本保留 `Copyright (c) .NET Foundation and Contributors`。SQLitePCLRaw bundle/core/provider 2.1.6 的版权声明为 `Copyright 2014-2023 SourceGear, LLC`；lib.e_sqlite3 2.1.13 为 `Copyright 2014-2024 SourceGear, LLC`。

`DOTNET-LICENSE-MIT.txt` 与以上 Microsoft 包、TraceEvent v3.1.21、Microsoft.Diagnostics.NETCore.Client 所标记提交、Microsoft.Data.Sqlite 所标记提交中的 MIT 条款文字一致。System.Memory 4.5.3 的旧 nuspec 使用许可 URL，其包内 `LICENSE.TXT` 同样是这份 MIT 文本。Apache-2.0 原文来自 SQLitePCLRaw v2.1.6，四个 SQLitePCLRaw 包均在各自 nuspec 中声明 Apache-2.0。

TraceEvent 包还带有按架构复制的 `KernelTraceControl.dll`、`msdia140.dll` 以及 COM 互操作程序集；不能只根据 `project.assets.json` 的 runtime 节点枚举发布文件。其包许可元数据为 MIT，相关包版权声明与 MIT 文本在此保留。

## 随附原文及来源

以下文件保留完整条款文字；为便于仓库检查，仅统一 UTF-8、换行和行末空白。上游整体 THIRD-PARTY-NOTICES 可能列有未进入本应用的组件或样例资源，完整保留这些说明不表示它们全部随本应用分发。

| 本目录文件 | 对应来源 |
| --- | --- |
| [DOTNET-LICENSE-MIT.txt](DOTNET-LICENSE-MIT.txt) | NuGet Microsoft.NETCore.App.Runtime.win-x64 8.0.30 的 `LICENSE.TXT`；与 WindowsDesktop 同版本及其他 Microsoft 包的 MIT 条款文字一致 |
| [DOTNET-RUNTIME-8.0.30-THIRD-PARTY-NOTICES.txt](DOTNET-RUNTIME-8.0.30-THIRD-PARTY-NOTICES.txt) | [Microsoft.NETCore.App.Runtime.win-x64 8.0.30](https://www.nuget.org/packages/Microsoft.NETCore.App.Runtime.win-x64/8.0.30) 包的 `THIRD-PARTY-NOTICES.TXT`；也对应 System.Text.Json 8.0.5 / ILLink 8.0.30 包内相同文本 |
| [WPF-8.0.30-THIRD-PARTY-NOTICES.txt](WPF-8.0.30-THIRD-PARTY-NOTICES.txt) | [dotnet/wpf v8.0.30](https://github.com/dotnet/wpf/blob/v8.0.30/THIRD-PARTY-NOTICES.TXT) |
| [WINFORMS-8.0.30-THIRD-PARTY-NOTICES.txt](WINFORMS-8.0.30-THIRD-PARTY-NOTICES.txt) | [dotnet/winforms v8.0.30](https://github.com/dotnet/winforms/blob/v8.0.30/THIRD-PARTY-NOTICES.TXT) |
| [DOTNET-8-PACKAGES-THIRD-PARTY-NOTICES.txt](DOTNET-8-PACKAGES-THIRD-PARTY-NOTICES.txt) | System.Collections.Immutable 8.0.0 / System.Reflection.Metadata 8.0.0 包内声明 |
| [DOTNET-6-PACKAGES-THIRD-PARTY-NOTICES.txt](DOTNET-6-PACKAGES-THIRD-PARTY-NOTICES.txt) | Microsoft.Extensions.* 6.0.0、System.Diagnostics.DiagnosticSource 6.0.0、System.Runtime.CompilerServices.Unsafe 6.0.0 包内相同文本 |
| [DOTNET-5-PACKAGES-THIRD-PARTY-NOTICES.txt](DOTNET-5-PACKAGES-THIRD-PARTY-NOTICES.txt) | Microsoft.Win32.Registry 5.0.0、Microsoft.NETCore.Platforms 5.0.0、System.Security.AccessControl 5.0.0、System.Security.Principal.Windows 5.0.0 包内相同文本 |
| [DOTNET-MEMORY-4.5.3-THIRD-PARTY-NOTICES.txt](DOTNET-MEMORY-4.5.3-THIRD-PARTY-NOTICES.txt) | System.Memory 4.5.3 包内声明 |
| [DOTNET-REFLECTION-4.7.0-THIRD-PARTY-NOTICES.txt](DOTNET-REFLECTION-4.7.0-THIRD-PARTY-NOTICES.txt) | System.Reflection.TypeExtensions 4.7.0 包内声明 |
| [SQLITEPCLRAW-APACHE-2.0.txt](SQLITEPCLRAW-APACHE-2.0.txt) | [SQLitePCLRaw v2.1.6 的 LICENSE.TXT](https://github.com/ericsink/SQLitePCL.raw/blob/v2.1.6/LICENSE.TXT)；2.1.13 原生包亦以 Apache-2.0 声明 |
| [WEBVIEW2-1.0.4191.47-LICENSE.txt](WEBVIEW2-1.0.4191.47-LICENSE.txt) | Microsoft.Web.WebView2 1.0.4191.47 包内 `LICENSE.txt`，与 [NuGet 官方许可页面](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4191.47/License)一致 |
| [WEBVIEW2-1.0.4191.47-NOTICE.txt](WEBVIEW2-1.0.4191.47-NOTICE.txt) | 同一 WebView2 NuGet 包的 `NOTICE.txt`，包含 ANTLR / StringTemplate 声明 |

MIT 上游核对点：[TraceEvent v3.1.21](https://github.com/microsoft/perfview/blob/v3.1.21/LICENSE.TXT)、[Diagnostics 提交 8c08c89](https://github.com/dotnet/diagnostics/blob/8c08c89a0643d31db91e119b1adb463be3e0ffe5/LICENSE.TXT)、[EF Core 提交 423b1a0](https://github.com/dotnet/efcore/blob/423b1a0d38036efcefdbf1dfb80955449eae1238/LICENSE.txt)。

## 自包含 .NET 与外部组件

内置 .NET 的 win-x64 包需要同时保留 .NET Runtime 与 Windows Desktop Runtime 的 MIT 许可及上面对应的 Runtime/WPF/WinForms 第三方声明。本清单按已还原的 **8.0.30** 运行时包收集，未使用 SDK 总目录中的 THIRD-PARTY-NOTICES 冒充运行时声明；使用其他运行时版本发布时必须重新核对。

Microsoft.Web.WebView2 **SDK** 提供本程序引用的托管程序集与 Loader，其当前包许可是上述 Microsoft BSD 三条款形式文本。完整浏览器 **WebView2 Runtime** 是单独安装的产品；SDK 的许可不等于 Runtime 的许可。本目录没有给 GPL 添加链接例外，也不把第三方组件重新声明为 GPL。

WebView2 Runtime、yt-dlp、FFmpeg 与 ffprobe 不捆绑在本发行包中，可由用户自行安装或通过工具的环境安装入口另行下载。下载的具体版本及构建选项决定其许可和再分发义务；如改为随包分发，应重新核对并提供适用声明与所需源代码材料。

网页下载能力受站点、登录权限、播放器与引擎支持范围影响，不承诺所有视频可下载，也不绕过 DRM。自动构建或本地样例通过不能证明真实站点兼容性。

公开 ZIP 应包括根目录 `LICENSE`、`NOTICE.md` 与本目录的全部文件。更新依赖后，请以新的还原图及最终发布文件重新核对版本、构建脚本额外复制项和许可文本。
