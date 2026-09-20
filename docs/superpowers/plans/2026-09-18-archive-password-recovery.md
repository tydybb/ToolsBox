# Archive Password Recovery Implementation Plan

> 历史记录：用户于 2026-09-18 要求移除压缩包密码找回功能，该功能现已撤下。本文的密码找回方案、测试结果和发布路径仅对应旧版；文件解锁全选功能仍保留。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate offline local ZIP/RAR/7z candidate verification using an embedded engine and explicit outcome states, without modifying or extracting the archive.

**Architecture:** Core provides lazy candidates and typed verification contracts. Windows hosts a fixed native engine in an isolated, bounded helper, using authenticated local IPC for passwords. WPF owns inputs, progress, cancellation and the shared drop router. Before building the full feature, qualify the native API against encrypted fixtures and stop to report any unsupported requirement.

**Tech Stack:** .NET 8 WPF, official x64 7-Zip 26.03 native engine, pinned SharpSevenZip 2.0.128 managed callback adapter, Windows process/token/job APIs, xUnit.

**Execution status (2026-09-18):** Tasks 1–4 implemented and independently reviewed; both EXE variants published and smoke-tested. The original checklist is retained below as planned scope, not a claim that every proposed fault-injection/manual test ran. Exact executed checks and remaining validation limits are recorded in `../2026-09-18-archive-recovery-verification.md`.

**Implementation adjustment:** Native qualification demonstrated that wrong passwords and encrypted-data/header damage overlap for some 7z/RAR responses. The user explicitly approved `RejectedUncertain`: preserve the warning and continue, requiring complete protected-data verification before Match. The qualified LGPL wrapper supplies the native callbacks; a duplicate custom COM wrapper is unnecessary. Engine resources/notices live together under `src/ToolsBox.Windows/Assets` and are embedded in both EXE variants.

### Task 1: Engine qualification (must pass before UI integration)

Files: `src/ToolsBox.Windows/ArchiveRecovery/SevenZipPasswordVerifier.cs`, `src/ToolsBox.Windows/ArchiveRecovery/SevenZipInterop.cs`, `src/ToolsBox.Windows/ArchiveRecovery/EmbeddedArchiveEngine.cs`, `src/ToolsBox.Windows/ArchiveRecovery/Assets/7z.dll`, `third-party/7zip/`, `tests/ToolsBox.App.Tests/ArchiveRecovery/ArchiveEngineTests.cs`, fixture files and their provenance.

- [ ] Download official fixed-version x64 archive/installer into a task-owned build folder; extract, do not install globally. Record engine SHA-256, version and applicable license/source links. Never use a random downloaded archive-recovery executable.
- [ ] Write tests for wrong-password then correct-password ZIP ZipCrypto/AES, 7z header/non-header encryption, RAR3/4 and RAR5 header/non-header fixtures. Add unchanged-file hash, unencrypted, damaged and unsupported/missing-volume outcomes. Use read-only streams and test/null output callbacks, never extraction paths.
- [x] Implement only the native adapter required by these tests. Password callback stays in process memory. Typed outcomes include user-approved `RejectedUncertain`; ambiguity is never Match. Read all protected data/checksums before Match. Preserve uncertain explanations/counts while continuing candidates; unsupported methods and failed/limited verification stop.
- [ ] Run tests and independently inspect callback implementation, native error mapping, provenance and permissions. Maintain a qualification report with per-format evidence.

### Task 2: Core contracts and candidate generation

Files: `src/ToolsBox.Core/ArchiveRecovery/ArchiveRecoveryModels.cs`, `PasswordCandidates.cs`, `IArchivePasswordVerifier.cs`, `ArchiveRecoveryRunner.cs`; `tests/ToolsBox.Core.Tests/ArchiveRecovery/PasswordCandidatesTests.cs`, `ArchiveRecoveryRunnerTests.cs`.

```csharp
public enum ArchivePasswordOutcome { Match, NoMatch, RejectedUncertain, NotEncrypted, Unsupported, InvalidArchive, Inconclusive }
public sealed record ArchivePasswordResult(ArchivePasswordOutcome Outcome, string Message);
public interface IArchivePasswordVerifier
{
    Task<ArchivePasswordResult> VerifyAsync(string path, string password, TimeSpan timeout, CancellationToken cancellationToken);
}
```

- [ ] Tests first: text lines preserve spaces/case and Unicode; UTF-8/BOM import is lazy; rule generation produces prefix+unknown+suffix in increasing-length order; zero unknown length is allowed; deduplicate custom characters and validate 0<=min<=max<=12. Use BigInteger for counts and never allocate search space.
- [ ] Implement `PasswordCandidates.FromText`, `FromFile`, `FromRules` using iterators and cancellation between candidates. Tests assert `FromRules("a", "z", "01", 0, 1)` yields `az,a0z,a1z`.
- [ ] Runner tests: exactly one match ends loop; NoMatch advances; user-approved RejectedUncertain (password mismatch versus encrypted-data corruption cannot be distinguished) advances while retaining an uncertainty count/message; unsupported, known corruption, timeout/inconclusive stop with useful status; cancellation releases worker and never exposes a partial password as success. Progress reports attempted count/elapsed time without password.
- [ ] Implement single-task, serial verifier loop and test empty/exhausted input, exception sanitization and repeat run. Commit after spec/quality review.

### Task 3: Isolated worker and engine lifecycle

Files: `src/ToolsBox.Windows/ArchiveRecovery/ArchiveRecoveryWorker.cs`, `ArchiveRecoveryClient.cs`, `ArchiveWorkerProcess.cs`; `src/ToolsBox.App/App.xaml.cs`; process/IPC tests in `tests/ToolsBox.App.Tests/ArchiveRecovery/`.

- [ ] Tests first: handshake mismatch, oversized/invalid message, worker exit, verification timeout, cancellation during connect/verify, re-run after stop, parent close and resource-limit failure. Verify only owned helpers are terminated, and no password appears in arguments/logs/environment.
- [ ] Worker receives only a pipe/session identifier in arguments; verify peer/session and current-user ownership. Request JSON includes path/password in authenticated IPC memory. Bound frames and use absolute verified engine path. Run worker before normal window startup and keep it windowless.
- [ ] Start helper with the least privileges needed for read-only target access; restrict inherited handles, assign owned kill-on-close job and memory limits before native parsing. Cancel pending reads and reap worker on all exit paths. Default per-candidate timeout=30 seconds; validate configurable 1..300 seconds.
- [ ] Embedded DLL extraction uses a controlled path, rejects reparse points, verifies fixed hash, and prevents replacement while loaded. Keep licenses available from application packaging.
- [ ] Run process lifecycle and actual engine tests as ordinary/elevated users. Commit after spec/quality review.

### Task 4: WPF tool and shared drag routing

Files: `src/ToolsBox.App/ArchiveRecovery/ArchiveRecoveryViewModel.cs`, `src/ToolsBox.App/Views/ArchiveRecoveryView.xaml`, `.xaml.cs`, `MainViewModel.cs`, `MainWindow.xaml`, `.xaml.cs`; WPF tests.

- [ ] Test VM states, command enabled states, single active task, progress dispatch, stop/restart, switching pages retaining progress, obscured result, explicit reveal/copy, import validation and rejection of directory/multiple paths.
- [ ] Add path browse/drop and dictionary/rule mode inputs; counts, timeout, start/stop, elapsed/rate and outcome. No automatic recovery on drop; no automatic extraction or clipboard write. Do not persist passwords.
- [ ] Extend existing navigation and shared ShellFileDropReceiver route rather than adding a second window registration. Keep selection highlight, file-unlocker drop and worker-headless startup tests.
- [ ] Run WPF integration and ordinary/elevated drop tests; review spec then quality.

### Task 5: Delivery

- [ ] Update README with exact qualified formats, exclusions, no-success guarantee, privacy and engine license/source details. Explain rules and large-search cost.
- [ ] Run `dotnet test ToolsBox.slnx -c Release` and actual installed-runtime plus self-contained worker smoke tests, checking input archive hashes and process cleanup.
- [ ] Publish profile PortableWinX64 to fresh output directories, then self-contained via `-p:SelfContained=true -p:EnableCompressionInSingleFile=true`. Confirm one EXE in each directory, native engine loading and license inclusion, and measure real sizes.
- [ ] Review complete diff against spec, commit scoped changes to main, push, and report verification gaps honestly. Do not call real Explorer mouse gestures tested unless actually exercised.
