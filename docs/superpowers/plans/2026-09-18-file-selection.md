# File Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add reversible whole-result selection without changing unlock safety semantics.

**Architecture:** FileLockEntry emits IsSelected property changes. FileUnlockerViewModel owns collection/item subscriptions and aggregate nullable selection. The header sends a toggle command instead of relying on three-state click cycling.

**Tech Stack:** .NET 8, WPF, xUnit.

**Execution status (2026-09-18):** Implemented and independently reviewed. Selection/WPF regression and full solution tests passed. The original checklist below records the intended procedure; actual delivery evidence and manual-test limits are in `../2026-09-18-archive-recovery-verification.md`.

### Task 1: Observable row selection and header toggle

Files: `src/ToolsBox.Core/FileUnlocking/FileLockEntry.cs`, `src/ToolsBox.App/FileUnlocking/FileUnlockerViewModel.cs`, `src/ToolsBox.App/Views/FileUnlockerView.xaml`, `src/ToolsBox.App/Views/FileUnlockerView.xaml.cs`, `tests/ToolsBox.App.Tests/Startup/FileUnlockerSelectionTests.cs`.

- [ ] Write failing tests on a VM with a fake scan service and three records, two sharing PID. Assert initial false, one selected gives null, `ToggleSelectAllCommand.Execute(null)` makes all true, second toggle clears, SelectedCount=3 and SelectedProcessCount=2 when all selected. While pending scan/action, command cannot execute; replacing scan results clears selection and detaches old rows. Dispose detaches subscriptions. Include actual WPF checkbox binding test.
- [ ] Run `dotnet test tests/ToolsBox.App.Tests -c Release --filter FullyQualifiedName~FileUnlockerSelectionTests`; verify missing behavior fails.
- [ ] Retain record constructor/equality compatibility; implement `INotifyPropertyChanged` for IsSelected with a backing field. VM exposes `bool? AllSelected`, `int SelectedCount`, `int SelectedProcessCount`, `bool CanSelectEntries`, and `RelayCommand ToggleSelectAllCommand`. Subscribe/unsubscribe to CollectionChanged and item PropertyChanged; track subscribed items to handle Reset. Use event batching during full-selection changes to avoid O(n²) recomputation.
- [ ] Header checkbox uses one-way IsChecked binding and toggle command; partial state click selects everything. Disable header and row selection while busy without disabling result scrolling. Show counts separately from status to avoid overlapping action buttons. Confirm dialogs mention selected unique processes/handles; do not remove existing warnings or safety checks.
- [ ] Run selection and existing FileUnlockerScanTests; then whole solution. Review spec compliance and code quality. Commit only scoped files, leaving AGENTS.md untouched.

Example contract assertions:

```csharp
Assert.Null(vm.AllSelected);
vm.ToggleSelectAllCommand.Execute(null);
Assert.True(vm.AllSelected);
Assert.Equal(3, vm.SelectedCount);
Assert.Equal(2, vm.SelectedProcessCount);
```
