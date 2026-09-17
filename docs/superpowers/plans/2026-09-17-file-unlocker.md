# File Unlocker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add safe and advanced file/folder unlocking to the existing .NET 8 WPF toolbox with text entry, browse, and drag-and-drop input.

**Architecture:** Put path matching and safety policy in Core, native handle discovery and privileged actions in Windows, and user interaction in App. Elevated actions re-open the target process and revalidate identity and path instead of trusting stale UI data.

**Tech Stack:** .NET 8, WPF, C#, xUnit, Windows NT system information and kernel handle APIs

---

### Task 1: Path matching and safety policy

**Files:**
- Create: `src/ToolsBox.Core/FileUnlocking/FileLockTarget.cs`
- Create: `src/ToolsBox.Core/FileUnlocking/FileLockEntry.cs`
- Create: `src/ToolsBox.Core/FileUnlocking/FileLockPathMatcher.cs`
- Create: `src/ToolsBox.Core/FileUnlocking/FileUnlockSafetyPolicy.cs`
- Test: `tests/ToolsBox.Core.Tests/FileUnlocking/FileLockPathMatcherTests.cs`
- Test: `tests/ToolsBox.Core.Tests/FileUnlocking/FileUnlockSafetyPolicyTests.cs`

- [ ] Write failing tests for exact-file matching, recursive folder boundary matching, case-insensitivity, path normalization, and protected processes.
- [ ] Run the focused tests and confirm they fail because the types are absent.
- [ ] Implement the minimal models, matcher, and safety policy.
- [ ] Re-run focused tests and confirm they pass.

### Task 2: Native Windows handle scanning

**Files:**
- Create: `src/ToolsBox.Core/FileUnlocking/IFileLockService.cs`
- Create: `src/ToolsBox.Windows/FileUnlocking/SystemHandleScanner.cs`
- Create: `src/ToolsBox.Windows/FileUnlocking/WindowsFileLockService.cs`
- Test: `tests/ToolsBox.Windows.Tests/FileUnlocking/SystemHandleScannerTests.cs`

- [ ] Write an integration test that opens a temporary file with `FileShare.None` and expects the current process handle to be discovered.
- [ ] Run the test and confirm it fails because the scanner is absent.
- [ ] Implement dynamic system handle buffer allocation, process handle caching, safe duplicate handles, DOS path resolution, and target matching.
- [ ] Re-run the integration and core tests.

### Task 3: Unlock actions and on-demand elevation

**Files:**
- Create: `src/ToolsBox.Core/FileUnlocking/FileUnlockResult.cs`
- Create: `src/ToolsBox.Windows/FileUnlocking/ElevatedActionRequest.cs`
- Modify: `src/ToolsBox.Windows/FileUnlocking/WindowsFileLockService.cs`
- Modify: `src/ToolsBox.App/App.xaml.cs`

- [ ] Add tests for stale process identity and protected-process rejection.
- [ ] Implement process termination with graceful error results.
- [ ] Implement revalidated `DUPLICATE_CLOSE_SOURCE` handle closing.
- [ ] Add `runas` fallback and hidden elevated action mode with encoded arguments and exit codes.

### Task 4: WPF tool navigation and drag/drop UI

**Files:**
- Create: `src/ToolsBox.App/MainViewModel.cs`
- Create: `src/ToolsBox.App/FileUnlocking/FileUnlockerViewModel.cs`
- Create: `src/ToolsBox.App/Views/PortMonitorView.xaml`
- Create: `src/ToolsBox.App/Views/FileUnlockerView.xaml`
- Modify: `src/ToolsBox.App/MainWindow.xaml`
- Modify: `src/ToolsBox.App/MainWindow.xaml.cs`

- [ ] Extract the existing port monitor into its own view.
- [ ] Add navigation between port monitor and file unlocker.
- [ ] Add text input, file/folder browse, drop zone, automatic scan, selectable results, status, and advanced action section.
- [ ] Add confirmation dialogs and refresh after every action.

### Task 5: Documentation and verification

**Files:**
- Modify: `README.md`

- [ ] Document file unlocking, elevation, and data-loss risks.
- [ ] Run all Release tests and require zero failures.
- [ ] Run the Release build and require zero errors and warnings.
- [ ] Launch the app and confirm it remains running without an immediate startup crash.
