# Framework-dependent single-file release implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Publish a Windows x64 single EXE without a bundled .NET runtime, preserving the official missing-runtime prompt and download link (user-approved option A).

**Architecture:** Keep the existing WinExe native apphost and administrator manifest. Runtime discovery happens before managed/WPF startup. Use a named publish profile; do not add a managed preflight check, a custom bootstrapper, automatic downloads, or installer changes.

**Tech Stack:** .NET 8, WPF, MSBuild publish profiles, PowerShell validation.

### Task 1: Preserve a reproducible release configuration

- [x] Add `tests/ToolsBox.App.Tests/Startup/FrameworkDependentPublishTests.cs` to assert the named profile is framework-dependent, single-file, win-x64, includes native dependencies, and keeps the GUI apphost. Run it before adding the profile and confirm failure.
- [x] Add `src/ToolsBox.App/Properties/PublishProfiles/PortableWinX64.pubxml` with `SelfContained=false`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `UseAppHost=true`, `PublishTrimmed=false`, `DebugType=none`, `DebugSymbols=false`, and output under `bin/publish/win-x64`.
- [x] Publish with `dotnet publish src/ToolsBox.App/ToolsBox.App.csproj -c Release -p:PublishProfile=PortableWinX64`. Check there is only one EXE and record its size.

### Task 2: Document and validate the native-host behavior

- [x] Update README with the publish command, output location, Windows x64 .NET 8 Desktop Runtime requirement, official download page, optional-English native prompt, no silent installation, and restart-after-install steps.
- [x] Add `scripts/Test-MissingRuntime.ps1`, an opt-in elevated diagnostic. Point only its child process's DOTNET_ROOT_X64 at an isolated hostfxr-only directory. Disable GUI dialogs only for the automated child so stderr and failure exit code can be checked for a framework requirement and official download URL. Keep global runtime installations and environment unchanged.
- [x] Run the missing-runtime diagnostic and the existing real network helper smoke check on the published EXE. A console error-path check is not a visual acceptance test of the prompt.
- [x] Run `dotnet test ToolsBox.slnx -c Release` using a separate output directory if an older app is running; run `git diff --check`. Preserve untracked AGENTS.md. Deliver using the established main-only commit/push workflow.

## Verification evidence

- Profile regression failed before the profile existed, then passed with it.
- Published directory contains only `宝哥工具箱.exe`: 12,254,135 bytes (11.69 MiB). Referenced-project PDBs are explicitly excluded from publish items.
- Isolated missing Core / missing Desktop runtime cases both returned framework-specific official x64 download links and exit code -2147450730. No global runtimes or environment settings changed.
- Published single-file helper: two real ETW UDP collection / stop cycles passed, read-only QoS query passed, clean shutdown passed.
- Full suite: 80 tests passed. Native GUI prompt appearance and clicking its download button on a clean Windows machine remain manual acceptance checks.
