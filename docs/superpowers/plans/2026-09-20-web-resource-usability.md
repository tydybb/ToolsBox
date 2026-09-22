# 网页资源下载交互改进

> 使用 subagent-driven-development 与 test-driven-development 执行；用户已确认范围。在 main 修改，不自动提交。

**目标：** 统一工具入口、移除链接导出、环境检测与缺失项安装、明确资源勾选。

**结构：** EnvironmentWindow 负责检测和安装；ComponentManager 保留校验并上报下载进度；WebResourceWindow 负责当前筛选的勾选与下载；MainViewModel 增加独立窗口工具的首页状态。

- [x] 环境子任务：新增环境窗口与运行时安装服务；离线检测浏览器和视频组件组，已有项跳过。下载显示实际百分比，校验/安装显示阶段。微软签名验证后运行安装器，不中途强杀。使用假安装后端测试，不更改本机运行时。
- [x] UI 测试先行：环境入口存在；筛选下全选仅影响可见项；隐藏勾选保留；表头反映部分/全部；网页导航复用现有样式。
- [x] UI 实现：IsSelected 绑定行复选框；下载仅当前可见勾选项；移除 CopyLinks/ExportLinks；筛选框定高居中单行；首页使用同一 RadioButton 样式和独立工具落地页。
- [x] 回归：运行 App 聚焦测试、排除危险句柄关闭用例的全套测试、普通权限浏览器 smoke；检查渲染截图。
- [x] 更新 README/验证记录；发布新的自包含 EXE，不覆盖运行中的旧文件。

验证命令：`dotnet test ToolsBox.slnx -c Release --no-restore --filter 'FullyQualifiedName!=ToolsBox.App.Tests.Startup.SystemHandleScannerTests.CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess'`

选择语义：全选、取消和下载均针对当前筛选；隐藏条目保留选择但不会意外加入此次下载；新识别资源默认不选中。检测不触发下载，用户点击安装后才安装缺失项。视频组件作为完整的解析/转码组件组检测和修复。
