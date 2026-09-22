# 收藏、历史与登录保留

用户已确认实施；在 main 工作区修改，不自动提交。使用测试驱动与独立存储子任务。

## 已确认设计

- 收藏：收藏当前页、列表打开、重命名和删除；历史按最近访问排序，同一地址去重，最多500条，单条删除/清空。
- 两者单独存入 `%LOCALAPPDATA%/ToolsBox/BrowserLibrary.json`，原子更新，保存失败不改变内存状态；加载损坏不静默覆盖。网址仅允许 HTTP(S)，不含用户名密码。
- 沿用原有 `%LOCALAPPDATA%/ToolsBox/WebProfile`，保留网站正常的持久 Cookie 和本地存储；不改变会话 Cookie 期限，不绕过网站失效策略，不把 Cookie 写入收藏文件。
- 仅成功的顶层导航完成记录历史；不记录资源请求。清空历史只操作历史记录，清除网站数据仍为独立警告操作。
- UI：浏览器顶部增加收藏当前页、收藏夹、历史记录入口；列表窗口提供选择打开和管理操作。

## 实施及验证

- [x] BrowserLibraryStore + 聚焦测试：重载保存、改名删除、500上限、去重、非法网址、文件损坏与保存失败保护。
- [x] 列表窗口及浏览器接入：先验证缺失入口的失败测试，再实现入口/成功导航记录；固定数据目录通过构造参数允许隔离验收，不迁移用户目录。
- [x] 真实浏览器分进程验证：隔离测试目录写入持久 Cookie、收藏及历史；关闭进程后第二次读取，清空历史再核对 Cookie 不受影响。
- [x] 安全全套测试排除真实关闭句柄测试；渲染截图；README和验证记录；新目录双版本发布。

安全测试命令：`dotnet test ToolsBox.slnx -c Release --no-restore --filter 'FullyQualifiedName!=ToolsBox.App.Tests.Startup.SystemHandleScannerTests.CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess'`。
