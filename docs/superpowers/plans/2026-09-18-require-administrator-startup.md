# Require Administrator at Startup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every 宝哥工具箱 launch request Windows administrator approval before the WPF main window starts.

**Architecture:** Embed one explicit Windows application manifest in `ToolsBox.App` and set its execution level to `requireAdministrator`. Keep the existing elevated helper command modes unchanged for compatibility, and document that the whole application now starts elevated.

**Tech Stack:** .NET 8, WPF, Windows application manifest, MSBuild, xUnit

---

## File map

- Create `src/ToolsBox.App/app.manifest`: Windows execution-level declaration.
- Modify `src/ToolsBox.App/ToolsBox.App.csproj`: embed the explicit application manifest.
- Create `tests/ToolsBox.App.Tests/Startup/AdministratorStartupManifestTests.cs`: repository configuration regression test.
- Modify `README.md`: replace on-demand elevation wording with startup elevation behavior.

### Task 1: Administrator application manifest

**Files:**
- Create: `tests/ToolsBox.App.Tests/Startup/AdministratorStartupManifestTests.cs`
- Create: `src/ToolsBox.App/app.manifest`
- Modify: `src/ToolsBox.App/ToolsBox.App.csproj`

- [ ] **Step 1: Write the failing configuration test**

Create a test that finds `ToolsBox.slnx` by walking upward from `AppContext.BaseDirectory`, loads `src/ToolsBox.App/app.manifest`, and asserts both the manifest execution level and the project reference:

```csharp
using System.Xml.Linq;

namespace ToolsBox.App.Tests.Startup;

public sealed class AdministratorStartupManifestTests
{
    [Fact]
    public void AppProject_EmbedsManifestThatRequiresAdministrator()
    {
        string root = FindRepositoryRoot();
        string manifestPath = Path.Combine(root, "src", "ToolsBox.App", "app.manifest");
        Assert.True(File.Exists(manifestPath), $"Missing application manifest: {manifestPath}");

        XDocument manifest = XDocument.Load(manifestPath);
        XElement executionLevel = Assert.Single(
            manifest.Descendants().Where(element => element.Name.LocalName == "requestedExecutionLevel"));
        Assert.Equal("requireAdministrator", executionLevel.Attribute("level")?.Value);
        Assert.Equal("false", executionLevel.Attribute("uiAccess")?.Value);

        XDocument project = XDocument.Load(Path.Combine(root, "src", "ToolsBox.App", "ToolsBox.App.csproj"));
        XElement applicationManifest = Assert.Single(
            project.Descendants().Where(element => element.Name.LocalName == "ApplicationManifest"));
        Assert.Equal("app.manifest", applicationManifest.Value);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ToolsBox.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the ToolsBox repository root.");
    }
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test .\tests\ToolsBox.App.Tests\ToolsBox.App.Tests.csproj -c Release --filter FullyQualifiedName~AdministratorStartupManifestTests -p:NuGetAudit=false
```

Expected: FAIL with `Missing application manifest` because `src/ToolsBox.App/app.manifest` does not exist.

- [ ] **Step 3: Add the minimal manifest and project property**

Create `src/ToolsBox.App/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="BaoGeToolsBox.app" />
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security>
      <requestedPrivileges>
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
```

Add this property inside the existing `PropertyGroup` in `src/ToolsBox.App/ToolsBox.App.csproj`:

```xml
<ApplicationManifest>app.manifest</ApplicationManifest>
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Step 2 command again.

Expected: PASS, with zero failures.

- [ ] **Step 5: Commit the manifest change**

```powershell
git add src/ToolsBox.App/app.manifest src/ToolsBox.App/ToolsBox.App.csproj tests/ToolsBox.App.Tests/Startup/AdministratorStartupManifestTests.cs
git commit -m "feat: require administrator permission at startup"
```

### Task 2: Documentation and complete verification

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update the elevation documentation**

Replace the file-unlocker and network-monitor statements that describe a normal-permission main process with these facts:

```markdown
宝哥工具箱启动时会显示 Windows UAC 提示，必须通过管理员授权后才能进入主界面。文件解锁、ETW 网络监控和上传限速因此可以直接使用所需的系统权限；现有辅助进程仍负责隔离危险操作和网络采集生命周期。
```

- [ ] **Step 2: Run full Release verification**

Run:

```powershell
dotnet clean .\ToolsBox.slnx -c Release -p:NuGetAudit=false
dotnet build .\ToolsBox.slnx -c Release --no-restore -p:NuGetAudit=false -v:minimal
dotnet test .\ToolsBox.slnx -c Release --no-build --no-restore -p:NuGetAudit=false --logger "console;verbosity=minimal"
```

Expected: build completes with 0 warnings and 0 errors; all Core, Windows, and App tests pass.

- [ ] **Step 3: Run Debug build verification**

Run:

```powershell
dotnet build .\ToolsBox.slnx -c Debug -p:NuGetAudit=false -v:minimal
```

Expected: build completes with 0 warnings and 0 errors.

- [ ] **Step 4: Verify embedded execution level without accepting UAC**

Locate the Windows SDK `mt.exe`, extract resource `#1` from `src/ToolsBox.App/bin/Release/net8.0-windows/宝哥工具箱.exe`, and assert the extracted XML contains:

```xml
<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
```

Do not automate the Windows UAC security dialog. A manual launch may be used only to confirm that the prompt appears; the user must approve or cancel it.

- [ ] **Step 5: Commit documentation and push main**

```powershell
git add README.md
git commit -m "docs: explain administrator startup requirement"
git push origin main
```
