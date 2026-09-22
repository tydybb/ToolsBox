# 两种单文件一键打包实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 提供内置 .NET 和不内置 .NET 的两个 Windows 打包入口，双击与终端均可用。

**Architecture:** 两个根目录 CMD 入口委托 `scripts/Publish-Portable.ps1`，复用现有 PortableWinX64 发布配置。脚本定位仓库、检查 SDK、串行发布、验证输出并报告路径；每次输出到模式目录下的新时间戳目录。用户已确认该方案并要求实施；继续在 main 工作，保留原有未提交改动，不提交、不推送。

**Tech Stack:** Windows CMD、Windows PowerShell 5.1、.NET SDK、现有 net8.0-windows / win-x64 发布配置。

---

## Task 1：打包入口与回归测试

**Files:**
- Create: `Publish-SelfContained.cmd`
- Create: `Publish-FrameworkDependent.cmd`
- Create: `scripts/Publish-Portable.ps1`
- Create: `tests/ToolsBox.App.Tests/Startup/OneClickPublishTests.cs`

- [x] 先编写测试并运行，确认新入口缺失时失败；使用隔离副本和受控的 dotnet 替身验证错误路径，不修改全局 PATH 或卸载 SDK。
- [x] 实现固定模式入口，使用 `%~dp0` 定位脚本；默认结束后暂停，`-NoPause` 支持无人值守终端调用。保留 PowerShell 返回码，不因 pause 改写成功/失败。

```bat
@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-Portable.ps1" -Mode SelfContained %*
exit /b %errorlevel%
```

FrameworkDependent 入口只替换固定模式。暂停及参数验证由共享脚本处理，原生命令失败必须返回非零。

- [x] 共享脚本使用 `$PSScriptRoot` 定位仓库并在仓库目录运行 SDK；检查可用 SDK，不把仅安装 Runtime 当作满足构建要求。缺失时显示官方 SDK 下载地址，不自动安装、不修改全局执行策略。
- [x] 明确指定 Release、win-x64、SelfContained 和压缩模式，让 publish 正常 restore；不使用可能复用错误 RID 资产的 `--no-restore`。

```powershell
$publishArguments = @('publish', $projectPath, '-c', 'Release', '-r', 'win-x64',
    '-p:PublishProfile=PortableWinX64', "-p:SelfContained=$selfContained",
    "-p:EnableCompressionInSingleFile=$selfContained", "-p:PublishDir=$publishDirectory")
& $dotnetCommand.Source @publishArguments
```

- [x] 输出分别为 `bin/publish/self-contained-win-x64/<时间戳唯一标识>/` 和 `bin/publish/framework-dependent-win-x64/<时间戳唯一标识>/`；禁止删除旧包、杀进程或复用已存在的输出目录。共享 obj 不能同时发布两模式，提供仓库内独占锁及明确提示。
- [x] 仅在 publish 退出码为 0 且目标 EXE 存在、非空时报告成功；显示完整路径和大小。失败保留诊断输出且不报告成功。
- [x] 检查脚本语法与行为测试，覆盖模式参数、非仓库工作目录、中文/空格路径、SDK 缺失、publish 失败、无有效产物、输出隔离、终端不暂停及退出码。测试模拟 SDK 只证明控制流，真实产物另行验证。

## Task 2：说明与实际验证

**Files:**
- Modify: `README.md` 的构建与发布说明（保留其他既有改动）。

- [x] 文档列出双击用法、终端 `-NoPause` 用法、SDK 前提、输出目录规则、失败处理以及两种模式的区别；说明不捆绑视频组件或完整 WebView2。

```powershell
.\Publish-FrameworkDependent.cmd -NoPause
.\Publish-SelfContained.cmd -NoPause
```

- [x] 在项目根目录及其他工作目录分别调用两个真实入口，顺序发布两种 EXE；不得将模拟测试产物作为交付物。
- [x] 检查实际目录只包含预期单文件 EXE，记录大小、SHA256；不自动启动工具箱或触发 UAC。
- [x] 运行打包相关的定向测试及 `git diff --check`；不用不带筛选的全量测试，避免实际关闭文件句柄的危险用例。
- [x] 先需求符合性审查，再代码质量审查；修复问题后重新验证。最终说明已验证范围、入口和实际产物位置。

## 边界

本任务不修改业务功能、悬浮下载按钮、版权/作者、收费或激活机制，不购买或安装证书，不修改许可证。发布使用当前工作区源码，不宣称未提交源码可由某个 Git 提交完整复现。构建通过不代表真实机器启动、UAC 或缺失运行环境弹窗已人工验收。

## 2026-09-20 验收记录

- 新增测试先 RED：13 项因为入口文件缺失而失败；实现后同一组 13 项全部通过。
- 主代理最终回归：`dotnet test tests/ToolsBox.App.Tests/ToolsBox.App.Tests.csproj --no-restore --filter 'FullyQualifiedName~OneClickPublishTests|FullyQualifiedName~FrameworkDependentPublishTests' --verbosity minimal`，14/14 通过，退出码 0。
- Windows PowerShell 语法检查通过，PS1 为 UTF-8 BOM。需求与质量独立审查通过；补测默认暂停期间锁已释放。
- 在仓库根目录实际执行 `Publish-FrameworkDependent.cmd -NoPause`，退出码 0；输出目录 `bin/publish/framework-dependent-win-x64/20260920-122210-805-57efae2b7c2446d9aeb675223643d0bc/`，只有 `宝哥工具箱.exe`，13,659,670 字节（13.03 MiB）。发布时的 runtimeconfig 使用 `frameworks`，依赖 .NET 8 Core / Desktop Runtime。
- 从 `scripts` 工作目录通过完整路径实际执行 `Publish-SelfContained.cmd -NoPause`，退出码 0；输出目录 `bin/publish/self-contained-win-x64/20260920-122238-051-31d9084625c248c998be970f7d7db2b1/`，只有 `宝哥工具箱.exe`，76,898,568 字节（73.34 MiB）。发布时的 runtimeconfig 使用 `includedFrameworks`，包含 .NET 8.0.30 Core / Desktop Runtime。
- 不内置版 SHA256：`5F1E8466DF72D80E5F6B3D7FCE37646651E648954AEFC981E64B4691380C19E4`。
- 内置版 SHA256：`FAE91C916FCFF61FD00BC724FD2C93A4FDDB70C6AFD3E42252BDCFE5FCB791CB`。
- 未运行工具箱 GUI、触发 UAC、做干净 Windows 缺失运行环境弹窗验收或执行危险句柄测试。未清理旧包、未提交或推送。
