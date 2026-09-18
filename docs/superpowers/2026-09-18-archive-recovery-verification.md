# 全选与压缩包密码找回验收记录

日期：2026-09-18。工作分支：main；未创建功能分支。

## 自动化结果

- `dotnet test ToolsBox.slnx -c Release --no-restore`：156 项通过，0 失败、0 跳过（Core 57、Windows 26、App 73）。
- 原生引擎专项 25 项：ZIP ZipCrypto/AES、RAR3/4 与 RAR5 数据/文件头加密、7z 数据/文件头加密；包括损坏、分卷、不支持方法、ZIP 注释内伪 EOCD、无加密内容和不确定错误分类。
- 文件解锁实际 WPF 表头/行绑定和 100 行虚拟化全选通过；选择计数、去重进程数、忙碌禁用、新扫描清空、事件退订及记录兼容性通过。
- 真实工作进程：错误候选后正确密码、只读源哈希、无窗口、取消、连接超时、关闭回收、受限令牌以及仅当前用户管道 ACL 均有自动化覆盖。
- 两个发布 EXE 均使用 `ToolsBox.ArchiveSmoke` 验证 9 个真实样本；各样本错误候选被拒绝、正确密码完整校验通过、原包 SHA-256 不变。第九个样本为包含空格、中文、字母、数字、符号的 7z 密码。
- 内置 .NET EXE 在测试子进程看不到已安装运行环境时仍通过上述 9 个样本及取消测试：`--without-installed-runtime`。
- 管理员 RunAs 调用链已通过 9 个样本及取消测试。另在真实管理员进程中执行 14 项令牌/IPC 回归，全部通过；记录在本机 `bin/archive-elevated-tests-apphost.log`。
- 不带运行环境版本的缺失环境诊断通过：缺少 .NET Core / Windows Desktop Runtime 时分别返回缺失框架与官方 x64 下载地址；仅隔离测试子进程，不卸载系统运行环境。
- 全选、压缩包行为规范和代码质量均经过独立审查，已修复审查发现的空行取消与合法 ZIP 注释误判问题。

## 发布物

每个目录仅包含一个 EXE。未覆盖旧的其他发布目录。

| 版本 | 路径 | 字节数 | 大小 |
| --- | --- | ---: | ---: |
| 依赖 .NET Desktop Runtime | `bin/publish/archive-win-x64/宝哥工具箱.exe` | 19,436,127 | 18.54 MiB |
| 内置 .NET | `bin/publish/archive-self-contained-win-x64/宝哥工具箱.exe` | 79,898,474 | 76.20 MiB |

SHA-256：

- 依赖运行环境：`3E139102E91D4FB6AFBFA047CBB904244926DDF1277C5605F14C242250961D24`
- 内置运行环境：`C7E2E321D27FA7572F6760B1FCC05A231A11DE459C7E424793B3C3DA6E2C6777`

## 实测驱动的启动修复

`requireAdministrator` 清单令低权限解析进程启动返回 Windows 740。改用 `asInvoker` 清单，但在创建主窗口前显式检查管理员身份并以 `runas` 重启；取消 UAC 不打开主窗口。无界面解析分支在提权前分流，仍受限运行。

管道 `CurrentUserOnly` 在 Windows 上还要求相同提权级别，因此实际管理员父进程到中等完整性子进程不能连接。改为仅当前用户 SID 的受保护 DACL、明确的 medium 标签和双向 PID/会话验证。标签使用 `LABEL_SECURITY_INFORMATION` 设置，不申请整份 SACL 的审计特权。参考 [PipeOptions](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions)、[安全信息访问权限](https://learn.microsoft.com/en-us/windows/win32/secauthz/security-information)。

管理员测试还复现了受限控制台 `dotnet.exe` 在进入托管代码前以 `0xC0000142` 退出的情况。调试调用若提供 `.dll`，现改用同目录 Windows apphost `.exe`，统一为发布版入口；在挂起状态提前持有进程句柄，保持进程身份及退出信息。修复后 14 项管理员专项回归全部通过。

## 验证边界

- 未手动操作真实资源管理器鼠标拖放、UAC 取消按钮或全新 Windows 的下载按钮；已有自动化窗口钩子、导航和启动路径检查不等同于这些人工手势验收。
- 未对用户真实占用进程执行终止/强制关句柄，未测试用户实际遗忘密码的压缩包，也不保证任意格式变体或损坏文件可找回。
- 未进行完整恶意压缩包模糊测试或逐条注入全部系统资源限制失败；隔离、内存上限和失败清理在实现及现有测试范围内核验。
- 穷举时间取决于候选规模、格式/加密参数和文件大小。示例每秒 1000 次只是数学假设，不是本机速度承诺。
