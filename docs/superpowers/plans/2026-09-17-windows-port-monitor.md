# Windows Port Monitor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an extensible .NET 8 WPF toolbox whose first module monitors local TCP/UDP IPv4/IPv6 endpoints and owning processes with selectable live refresh.

**Architecture:** Keep domain/filter behavior in a UI-independent core library, Windows IP Helper API access in a Windows adapter, and WPF presentation in an MVVM application. The app consumes the collector through `IPortSnapshotProvider`, so later tools and alternate collectors remain isolated.

**Tech Stack:** .NET 8, WPF, C#, xUnit, Windows IP Helper API via P/Invoke

---

### Task 1: Solution and domain model

**Files:**
- Create: `ToolsBox.sln`
- Create: `src/ToolsBox.Core/ToolsBox.Core.csproj`
- Create: `src/ToolsBox.Core/Ports/PortEntry.cs`
- Create: `src/ToolsBox.Core/Ports/IPortSnapshotProvider.cs`
- Create: `tests/ToolsBox.Core.Tests/ToolsBox.Core.Tests.csproj`
- Create: `tests/ToolsBox.Core.Tests/Ports/PortEntryFilterTests.cs`

- [ ] Write tests proving blank filters return all entries and keywords match port, PID, process, protocol, state, and address.
- [ ] Run `dotnet test tests/ToolsBox.Core.Tests/ToolsBox.Core.Tests.csproj` and confirm compilation fails because filtering does not exist.
- [ ] Implement immutable `PortEntry`, `IPortSnapshotProvider`, and `PortEntryFilter.Apply`.
- [ ] Re-run the test project and confirm all filter tests pass.

### Task 2: Windows port snapshot provider

**Files:**
- Create: `src/ToolsBox.Windows/ToolsBox.Windows.csproj`
- Create: `src/ToolsBox.Windows/Ports/NativeMethods.cs`
- Create: `src/ToolsBox.Windows/Ports/WindowsPortSnapshotProvider.cs`
- Create: `tests/ToolsBox.Windows.Tests/ToolsBox.Windows.Tests.csproj`
- Create: `tests/ToolsBox.Windows.Tests/Ports/WindowsPortSnapshotProviderTests.cs`

- [ ] Write an integration test requiring a non-empty snapshot whose entries have valid protocols, address families, ports, and PIDs.
- [ ] Run the Windows test and confirm it fails because the provider is absent.
- [ ] Implement two-call buffer allocation for `GetExtendedTcpTable` and `GetExtendedUdpTable`, covering IPv4 and IPv6 owner-PID table classes.
- [ ] Parse native rows, convert network byte order ports and IPv6 byte arrays, and resolve process names without failing the snapshot.
- [ ] Re-run Windows and core tests and confirm they pass.

### Task 3: Refresh coordinator

**Files:**
- Create: `src/ToolsBox.Core/Ports/PortMonitorService.cs`
- Create: `tests/ToolsBox.Core.Tests/Ports/PortMonitorServiceTests.cs`

- [ ] Write tests proving successful refresh replaces the snapshot and failed refresh keeps the previous snapshot with an error message.
- [ ] Run the focused tests and confirm failure because `PortMonitorService` is absent.
- [ ] Implement serialized asynchronous refresh with observable state properties.
- [ ] Re-run all core tests and confirm green output.

### Task 4: WPF shell and port monitor UI

**Files:**
- Create: `src/ToolsBox.App/ToolsBox.App.csproj`
- Create: `src/ToolsBox.App/App.xaml`
- Create: `src/ToolsBox.App/App.xaml.cs`
- Create: `src/ToolsBox.App/MainWindow.xaml`
- Create: `src/ToolsBox.App/MainWindow.xaml.cs`
- Create: `src/ToolsBox.App/Ports/PortMonitorViewModel.cs`
- Create: `src/ToolsBox.App/Infrastructure/AsyncRelayCommand.cs`
- Create: `src/ToolsBox.App/Infrastructure/ObservableObject.cs`

- [ ] Implement a two-column toolbox shell with a single port-monitor navigation item.
- [ ] Bind filter text, interval options, pause/continue, immediate refresh, status, and a sortable read-only data grid.
- [ ] Use `PeriodicTimer` cancellation to restart auto-refresh when the interval changes and prevent overlapping refreshes.
- [ ] Verify startup performs an immediate refresh and window close cancels background work.

### Task 5: Documentation and verification

**Files:**
- Create: `README.md`
- Create: `.gitignore`

- [ ] Document requirements, startup command, UI behavior, permission limitations, and extension structure.
- [ ] Run `dotnet test ToolsBox.sln --configuration Release` and require all tests to pass.
- [ ] Run `dotnet build ToolsBox.sln --configuration Release --no-restore` and require zero errors and zero warnings.
- [ ] Launch the app briefly and confirm the process remains running without an immediate startup crash.
