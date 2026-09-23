# 多账号打卡目录选择实施计划

已获用户批准，在 main 实施，不提交、不读取真实账号消息。

目标：多目录弹窗、逐项独立检查、明确选择后保存及导入已有有效结果。

- Core/DingTalkDataLocator：保留 Locate 的不自动选账号语义，增加 Candidates，测试两个目录均返回。
- App/AttendancePreview：普通后台接收独立请求，按 Guid 匹配最小结果，不写正式状态；客户端限时并支持窗口关闭取消。后台不得处理过期、未授权请求。
- App/AttendanceAccountPickerWindow：滚动目录卡片，完整路径，立即检查、确定路径；每次只执行一项，错误可重试。已关闭窗口不再响应。
- AttendancePanel：找到目录即进入弹窗（2026-09-23 定稿：单账号同样弹窗确认并可预览检查，替代原“单账号回填”方案，与 README 一致）；只有选择后保存目录，合格同日结果写正式快照，由原 Bridge 导入/询问冲突。
- tests：多目录、结果隔离、关闭取消、过期结果、未启用拒绝、选择保存和 UI 控件测试；运行安全回归及发布两种 EXE。

验证命令：dotnet test ToolsBox.slnx -c Release --no-restore --filter FullyQualifiedName!=ToolsBox.App.Tests.Startup.SystemHandleScannerTests.CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess
