# Network Traffic Monitor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an elevated ETW-based per-application network monitor and persistent per-executable upload throttling to 宝哥工具箱.

**Architecture:** Keep traffic aggregation and bandwidth-rule contracts in `ToolsBox.Core`, put ETW/process/QoS/IPC implementations in `ToolsBox.Windows`, and add a third MVVM tool page in `ToolsBox.App`. The normal WPF process starts one on-demand elevated copy of itself and communicates through an authenticated named pipe; the helper owns ETW and QoS while the UI owns presentation state.

**Tech Stack:** .NET 8, WPF, xUnit, Windows ETW via `Microsoft.Diagnostics.Tracing.TraceEvent`, Windows `NetQos` PowerShell module, named pipes, JSON.

---

## File map

### Core domain

- Create `src/ToolsBox.Core/NetworkTraffic/NetworkTrafficDirection.cs`: upload/download enum.
- Create `src/ToolsBox.Core/NetworkTraffic/NetworkTrafficDelta.cs`: timestamped PID byte delta.
- Create `src/ToolsBox.Core/NetworkTraffic/ProcessIdentity.cs`: PID plus start time identity.
- Create `src/ToolsBox.Core/NetworkTraffic/ProcessTrafficSnapshot.cs`: immutable per-process snapshot.
- Create `src/ToolsBox.Core/NetworkTraffic/ApplicationTrafficSnapshot.cs`: immutable per-path aggregate.
- Create `src/ToolsBox.Core/NetworkTraffic/ProcessMetadata.cs`: process name/path/start-time metadata.
- Create `src/ToolsBox.Core/NetworkTraffic/INetworkTrafficSource.cs`: streaming source lifecycle.
- Create `src/ToolsBox.Core/NetworkTraffic/IProcessMetadataProvider.cs`: process resolution boundary.
- Create `src/ToolsBox.Core/NetworkTraffic/NetworkTrafficAggregator.cs`: one-second rates and session totals.
- Create `src/ToolsBox.Core/NetworkTraffic/NetworkTrafficFilter.cs`: search/activity filter and speed sort.
- Create `src/ToolsBox.Core/NetworkTraffic/BandwidthLimitRule.cs`: persistent rule model.
- Create `src/ToolsBox.Core/NetworkTraffic/IBandwidthLimitService.cs`: query/set/remove contract.

### Windows platform

- Modify `src/ToolsBox.Windows/ToolsBox.Windows.csproj`: add TraceEvent package.
- Create `src/ToolsBox.Windows/NetworkTraffic/EtwNetworkTrafficSource.cs`: elevated ETW consumer.
- Create `src/ToolsBox.Windows/NetworkTraffic/WindowsProcessMetadataProvider.cs`: safe process metadata lookup.
- Create `src/ToolsBox.Windows/NetworkTraffic/QosPolicyName.cs`: stable owned rule naming.
- Create `src/ToolsBox.Windows/NetworkTraffic/PowerShellQosCommandRunner.cs`: fixed-script NetQos runner.
- Create `src/ToolsBox.Windows/NetworkTraffic/WindowsBandwidthLimitService.cs`: rule ownership and conflict checks.
- Create `src/ToolsBox.Windows/NetworkTraffic/NetworkHelperMessage.cs`: versioned IPC envelopes.
- Create `src/ToolsBox.Windows/NetworkTraffic/LengthPrefixedJsonPipe.cs`: bounded framed JSON transport.
- Create `src/ToolsBox.Windows/NetworkTraffic/ElevatedNetworkHelper.cs`: ETW/QoS command host.
- Create `src/ToolsBox.Windows/NetworkTraffic/ElevatedNetworkClient.cs`: normal-process helper controller.

### WPF application

- Modify `src/ToolsBox.App/App.xaml.cs`: dispatch elevated-network mode.
- Modify `src/ToolsBox.App/MainWindow.xaml.cs`: compose network dependencies.
- Modify `src/ToolsBox.App/MainWindow.xaml`: add navigation and DataTemplate.
- Modify `src/ToolsBox.App/MainViewModel.cs`: own and navigate to the new tool.
- Create `src/ToolsBox.App/NetworkTraffic/NetworkTrafficViewModel.cs`: monitor lifecycle and UI snapshots.
- Create `src/ToolsBox.App/NetworkTraffic/ApplicationTrafficItemViewModel.cs`: application row and process children.
- Create `src/ToolsBox.App/NetworkTraffic/ProcessTrafficItemViewModel.cs`: PID row.
- Create `src/ToolsBox.App/NetworkTraffic/ByteRateFormatter.cs`: compact byte/rate labels.
- Create `src/ToolsBox.App/Views/NetworkTrafficView.xaml` and `.xaml.cs`: monitor page.
- Create `src/ToolsBox.App/Views/BandwidthLimitWindow.xaml` and `.xaml.cs`: upload-limit editor.
- Modify `README.md`: document monitoring, UAC, upload-only limitation, and cleanup.

### Tests

- Create `tests/ToolsBox.Core.Tests/NetworkTraffic/NetworkTrafficAggregatorTests.cs`.
- Create `tests/ToolsBox.Core.Tests/NetworkTraffic/NetworkTrafficFilterTests.cs`.
- Create `tests/ToolsBox.Core.Tests/NetworkTraffic/BandwidthLimitRuleTests.cs`.
- Create `tests/ToolsBox.Windows.Tests/NetworkTraffic/QosPolicyNameTests.cs`.
- Create `tests/ToolsBox.Windows.Tests/NetworkTraffic/LengthPrefixedJsonPipeTests.cs`.
- Create `tests/ToolsBox.Windows.Tests/NetworkTraffic/WindowsProcessMetadataProviderTests.cs`.
- Create `tests/ToolsBox.Windows.Tests/NetworkTraffic/WindowsBandwidthLimitServiceTests.cs`.
- Create `tests/ToolsBox.App.Tests/ToolsBox.App.Tests.csproj`: Windows-targeted xUnit project referencing the WPF app.
- Create `tests/ToolsBox.App.Tests/NetworkTraffic/NetworkTrafficViewModelTests.cs`: fake-source lifecycle and refresh tests.

## Task 1: Core traffic models and aggregation

**Files:** Core model and aggregator files listed above; `tests/ToolsBox.Core.Tests/NetworkTraffic/NetworkTrafficAggregatorTests.cs`.

- [ ] **Step 1: Write failing aggregation tests**

Cover upload/download separation, multiple PIDs under one normalized executable path, one-second rates, retained totals after exit, and PID reuse. The central assertion should have this shape:

```csharp
aggregator.Apply([
    new NetworkTrafficDelta(120, start, NetworkTrafficDirection.Upload, 2048, sampleAt),
    new NetworkTrafficDelta(121, secondStart, NetworkTrafficDirection.Download, 4096, sampleAt)
]);

IReadOnlyList<ApplicationTrafficSnapshot> result = await aggregator.CreateSnapshotAsync(sampleAt.AddSeconds(1));
ApplicationTrafficSnapshot app = Assert.Single(result);
Assert.Equal(2048, app.UploadBytesPerSecond);
Assert.Equal(4096, app.DownloadBytesPerSecond);
Assert.Equal(2, app.Processes.Count);
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run `dotnet test tests/ToolsBox.Core.Tests/ToolsBox.Core.Tests.csproj -c Release --filter FullyQualifiedName~NetworkTrafficAggregatorTests`. Expect compilation failures because the network traffic types do not exist.

- [ ] **Step 3: Implement minimal immutable models and aggregator**

Use the following public contract:

```csharp
public sealed class NetworkTrafficAggregator
{
    public NetworkTrafficAggregator(IProcessMetadataProvider metadataProvider);
    public void BeginSession(DateTimeOffset startedAt);
    public void Apply(IEnumerable<NetworkTrafficDelta> deltas);
    public Task<IReadOnlyList<ApplicationTrafficSnapshot>> CreateSnapshotAsync(
        DateTimeOffset sampledAt,
        CancellationToken cancellationToken = default);
    public void MarkStopped(DateTimeOffset stoppedAt);
}
```

Normalize application keys with `Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()`. Fall back to a stable restricted key containing process name, PID, and start time. Calculate each rate from bytes added since the previous snapshot divided by elapsed seconds, and clamp negative or zero elapsed time to zero rate.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Task 1 filter again. Expect all aggregation tests to pass.

- [ ] **Step 5: Commit**

Commit only Task 1 files with `git commit -m "feat: add network traffic aggregation domain"`.

## Task 2: Filtering and bandwidth-rule domain

**Files:** `NetworkTrafficFilter.cs`, `BandwidthLimitRule.cs`, `IBandwidthLimitService.cs`, and their Core tests.

- [ ] **Step 1: Write failing filter and rule tests**

Verify case-insensitive matching across application name, full path, and PID; active-only filtering; descending combined speed; preset/custom unit conversion; and invalid non-positive values.

```csharp
Assert.Equal(8_388_608UL, BandwidthLimitRule.ToBitsPerSecond(1, BandwidthUnit.MegabytesPerSecond));
Assert.Throws<ArgumentOutOfRangeException>(() =>
    BandwidthLimitRule.ToBitsPerSecond(0, BandwidthUnit.KilobytesPerSecond));
```

- [ ] **Step 2: Run and verify RED**

Run `dotnet test tests/ToolsBox.Core.Tests/ToolsBox.Core.Tests.csproj -c Release --filter FullyQualifiedName~NetworkTraffic`. Expect missing filter/rule types.

- [ ] **Step 3: Implement filter and service contract**

Expose:

```csharp
[Flags]
public enum BandwidthDirection { None = 0, Upload = 1, Download = 2 }
public enum BandwidthUnit { KilobytesPerSecond, MegabytesPerSecond }

public sealed record BandwidthLimitRule(
    string RuleName,
    string ExecutablePath,
    BandwidthDirection Direction,
    ulong BitsPerSecond,
    bool IsOwned,
    bool HasConflict);

public interface IBandwidthLimitService
{
    BandwidthDirection SupportedDirections { get; }
    Task<IReadOnlyList<BandwidthLimitRule>> GetRulesAsync(CancellationToken cancellationToken);
    Task<BandwidthLimitRule> SetUploadLimitAsync(string executablePath, ulong bitsPerSecond, CancellationToken cancellationToken);
    Task RemoveUploadLimitAsync(string executablePath, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Run and verify GREEN**

Run the Task 2 filter. Expect all network Core tests to pass.

- [ ] **Step 5: Commit**

Commit with `git commit -m "feat: add network traffic filtering and limit rules"`.

## Task 3: Process metadata and ETW source

**Files:** Windows csproj, `WindowsProcessMetadataProvider.cs`, `EtwNetworkTrafficSource.cs`, and metadata tests.

- [ ] **Step 1: Add TraceEvent dependency and write failing metadata tests**

Run `dotnet add src/ToolsBox.Windows/ToolsBox.Windows.csproj package Microsoft.Diagnostics.Tracing.TraceEvent`. Test the current process resolves to its PID, start time, name, and nonempty executable path; test a definitely absent PID returns restricted metadata without throwing.

- [ ] **Step 2: Run and verify RED**

Run `dotnet test tests/ToolsBox.Windows.Tests/ToolsBox.Windows.Tests.csproj -c Release --filter FullyQualifiedName~WindowsProcessMetadataProviderTests`. Expect the provider to be missing.

- [ ] **Step 3: Implement safe metadata lookup**

Implement `IProcessMetadataProvider.GetAsync(int pid, DateTimeOffset? eventStartTime, CancellationToken)` using `Process.GetProcessById`. Catch `ArgumentException`, `InvalidOperationException`, `Win32Exception`, and access failures. Cache successful records by `(PID, StartTime)` and never treat PID alone as permanent identity.

- [ ] **Step 4: Implement ETW source behind the Core interface**

Create a unique session name beginning with `BaoGeToolsBox-Network-`, require elevation, enable `KernelTraceEventParser.Keywords.NetworkTCPIP`, and subscribe to TCP/UDP send/receive events. Convert callbacks to bounded-channel deltas:

```csharp
private void Publish(int pid, NetworkTrafficDirection direction, int size, DateTime timestamp)
{
    if (pid <= 0 || size <= 0) return;
    if (!_channel.Writer.TryWrite(new NetworkTrafficDelta(pid, null, direction, size, timestamp)))
        Interlocked.Increment(ref _droppedEvents);
}
```

Expose dropped-event count in source status, stop the trace source before disposing the session, and make Stop idempotent.

- [ ] **Step 5: Run tests and build Windows project**

Run the focused metadata test and `dotnet build src/ToolsBox.Windows/ToolsBox.Windows.csproj -c Release`. Expect zero failures and zero warnings.

- [ ] **Step 6: Commit**

Commit with `git commit -m "feat: capture process network traffic with ETW"`.

## Task 4: Safe persistent QoS management

**Files:** `QosPolicyName.cs`, `PowerShellQosCommandRunner.cs`, `WindowsBandwidthLimitService.cs`, and Windows QoS tests.

- [ ] **Step 1: Write failing ownership and command-runner tests**

Verify identical normalized paths create the same `BaoGeToolsBox-` SHA-256-derived name, different paths create different names, foreign rules cannot be removed, conflicts are surfaced, and raw path text never appears in a generated PowerShell command string.

```csharp
string first = QosPolicyName.ForPath(@"C:\Apps\Demo.exe");
string second = QosPolicyName.ForPath(@"c:\apps\demo.exe");
Assert.Equal(first, second);
Assert.StartsWith("BaoGeToolsBox-", first);
```

- [ ] **Step 2: Run and verify RED**

Run `dotnet test tests/ToolsBox.Windows.Tests/ToolsBox.Windows.Tests.csproj -c Release --filter FullyQualifiedName~Qos`. Expect missing types.

- [ ] **Step 3: Implement a fixed-script runner**

The runner accepts an operation enum and typed values. Put rule name, executable path, and numeric rate into child-process environment variables. Invoke only constant scripts using `powershell.exe -NoProfile -NonInteractive -Command`. Set/query/remove scripts call `New-NetQosPolicy`, `Set-NetQosPolicy`, `Get-NetQosPolicy`, and `Remove-NetQosPolicy`, returning compressed JSON. Reject null characters, nonexistent paths for new rules, zero rates, and rule names without the owned prefix.

- [ ] **Step 4: Implement service ownership and conflict behavior**

Query the real effective rule list after each mutation. Update an existing owned rule, refuse to overwrite a foreign matching rule, and remove only when both the owned name and normalized path match.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the Task 4 filter. Unit tests use a fake command runner and must not modify machine QoS state.

- [ ] **Step 6: Commit**

Commit with `git commit -m "feat: manage persistent application upload limits"`.

## Task 5: Authenticated framed IPC and elevated helper

**Files:** IPC/client/helper files, `App.xaml.cs`, and pipe tests.

- [ ] **Step 1: Write failing in-memory pipe tests**

Test fragmented reads, multiple frames, oversized-frame rejection, cancellation, version mismatch, and token rejection. Use a payload larger than a typical pipe buffer to prove framing is not message-boundary dependent.

- [ ] **Step 2: Run and verify RED**

Run `dotnet test tests/ToolsBox.Windows.Tests/ToolsBox.Windows.Tests.csproj -c Release --filter FullyQualifiedName~LengthPrefixedJsonPipeTests`. Expect missing transport types.

- [ ] **Step 3: Implement framed transport**

Prefix UTF-8 JSON with a four-byte little-endian length. Enforce a 1 MiB maximum frame, use `ReadExactlyAsync`, serialize writes through `SemaphoreSlim`, and define envelopes:

```csharp
public sealed record NetworkHelperMessage(
    int Version,
    string Type,
    string? RequestId,
    JsonElement Payload);
```

Use protocol version `1`; reject every other version before dispatch.

- [ ] **Step 4: Implement normal-process client**

Create a random pipe name and 32-byte token, secure the server to the current user SID, start the current executable with `runas` and `--elevated-network <pipe> <token>`, accept with a timeout, then require a handshake before sending commands. Maintain one read loop, correlate command responses by request ID, and expose traffic batches through an event or channel.

- [ ] **Step 5: Implement elevated helper and app dispatch**

In `App.OnStartup`, recognize exactly three application arguments: `--elevated-network`, pipe name, and token. Connect to the pipe, authenticate, then dispatch `start-monitor`, `stop-monitor`, `get-rules`, `set-upload-limit`, `remove-upload-limit`, and `shutdown`. Use one bounded outgoing queue so only one writer touches the pipe. On disconnect, stop ETW and exit.

- [ ] **Step 6: Run pipe tests and full build**

Run the focused tests and `dotnet build ToolsBox.slnx -c Release`. Expect all to pass without starting UAC in tests.

- [ ] **Step 7: Commit**

Commit with `git commit -m "feat: add elevated network helper IPC"`.

## Task 6: Network traffic ViewModel

**Files:** all `src/ToolsBox.App/NetworkTraffic` files plus `tests/ToolsBox.App.Tests/ToolsBox.App.Tests.csproj` and `tests/ToolsBox.App.Tests/NetworkTraffic/NetworkTrafficViewModelTests.cs`.

- [ ] **Step 1: Create the App test project and write failing lifecycle tests**

Create a `net8.0-windows` xUnit project with `UseWPF=true`, reference `ToolsBox.App`, add it to `ToolsBox.slnx`, and use fake traffic/limit/metadata services. Verify start begins one session, a one-second tick publishes sorted rows, stop preserves totals, a second start resets totals, helper failure preserves the last snapshot, and repeated Dispose is safe.

Run `dotnet test tests/ToolsBox.App.Tests/ToolsBox.App.Tests.csproj -c Release --filter FullyQualifiedName~NetworkTrafficViewModelTests`. Expect compilation failure because the ViewModel does not exist.

- [ ] **Step 2: Add testable ViewModel seams**

Keep `NetworkTrafficViewModel` dependent only on `INetworkTrafficSource`, `IBandwidthLimitService`, `IProcessMetadataProvider`, and an injected clock. Expose `StartCommand`, `StopCommand`, `SetLimitCommand`, `RemoveLimitCommand`, `SearchText`, `ShowActiveOnly`, `Items`, `StatusText`, `LastSampleText`, and `HasDroppedEvents`.

- [ ] **Step 3: Implement one-second batched refresh**

Read deltas off the UI thread, apply them to `NetworkTrafficAggregator`, and marshal one immutable snapshot per second to the WPF dispatcher. Reuse row ViewModels by normalized application key so expansion/selection survives refresh; replace child PID rows by process identity.

- [ ] **Step 4: Implement display formatting and limit commands**

Format bytes using B/KB/MB/GB and rates with `/s`. Disable limit actions when the path is unavailable or a foreign conflict exists. After each mutation, query rules again and update every matching row.

- [ ] **Step 5: Run ViewModel tests and verify GREEN**

Run the Task 6 test filter. Expect all lifecycle, reset, failure, sorting, and disposal tests to pass without UAC.

- [ ] **Step 6: Verify compile and lifecycle manually without elevation**

Build the App project. Instantiate and dispose the ViewModel with fake dependencies in a small test harness or debugger; confirm repeated Dispose and stop calls do not throw.

- [ ] **Step 7: Commit**

Commit with `git commit -m "feat: add network traffic monitor view model"`.

## Task 7: WPF network page and navigation

**Files:** network views plus MainWindow/MainViewModel composition files.

- [ ] **Step 1: Add navigation and DataTemplate**

Add a third sidebar button labeled `网络流量`, a `DataTemplate` for `NetworkTrafficViewModel`, and `ShowNetworkTrafficCommand`. Preserve the existing port monitor and file unlocker initialization behavior.

- [ ] **Step 2: Build the monitor page**

Use a toolbar with start/stop, search, active-only checkbox, status, and sample time. Use a `DataGrid` with columns for application, upload rate, download rate, uploaded total, downloaded total, process count, and upload limit. Use `RowDetailsTemplate` for the PID child grid. Bind status warnings for access restrictions, helper disconnect, and dropped ETW events.

- [ ] **Step 3: Build the upload-limit dialog**

Provide preset buttons for 128 KB/s, 512 KB/s, 1 MB/s, and 5 MB/s plus positive numeric custom input and KB/s or MB/s unit selection. The dialog title and explanatory text must state that only upload is limited.

- [ ] **Step 4: Build and perform a non-elevated XAML smoke check**

Run `dotnet build src/ToolsBox.App/ToolsBox.App.csproj -c Release`, start `宝哥工具箱.exe`, verify the main window title and three navigation buttons, then close it. Expect no XAML parse exception.

- [ ] **Step 5: Commit**

Commit with `git commit -m "feat: add network traffic monitoring interface"`.

## Task 8: Documentation, automated regression, and elevated acceptance

**Files:** `README.md` and any targeted fixes discovered during acceptance.

- [ ] **Step 1: Update README**

Document ETW monitoring, per-application/PID display, session-only totals, UAC behavior, persistent upload-only QoS rules, the absence of download limiting, and how to remove a rule from the UI.

- [ ] **Step 2: Run the complete clean verification**

Run:

```powershell
dotnet clean ToolsBox.slnx -c Release
dotnet build ToolsBox.slnx -c Release --no-restore
dotnet test ToolsBox.slnx -c Release --no-build --no-restore
```

Expect zero build errors, zero warnings, and all tests passing.

- [ ] **Step 3: Perform elevated monitoring acceptance**

Start monitoring and approve one UAC prompt. Generate TCP and UDP traffic from at least two user applications. Confirm application totals equal their visible PID totals, rates fall to zero after traffic stops, and stopping retains session totals.

- [ ] **Step 4: Perform reversible QoS acceptance**

Choose a disposable test executable path, set a temporary upload limit, verify the owned rule with `Get-NetQosPolicy`, generate upload traffic, remove the limit, and verify the exact `BaoGeToolsBox-...` rule no longer exists. Wrap verification in `try/finally`; if cleanup fails, report the exact rule name rather than hiding it.

- [ ] **Step 5: Verify shutdown cleanup**

Close 宝哥工具箱 and confirm no elevated helper process and no `BaoGeToolsBox-Network-...` ETW session remain.

- [ ] **Step 6: Commit and synchronize main**

Commit README or acceptance fixes with `git commit -m "docs: document network monitoring and upload limits"`. Confirm `git status --short --branch` contains only the pre-existing untracked `AGENTS.md`, then push `main` to `origin/main`.
