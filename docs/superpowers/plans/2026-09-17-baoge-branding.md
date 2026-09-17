# Baoge Toolbox Branding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the user-facing Windows application to 宝哥工具箱 and add a production-ready multi-size hammer icon.

**Architecture:** Keep code identities stable while changing WPF branding and assembly metadata. Generate one high-resolution source image and derive the Windows ICO deterministically.

**Tech Stack:** .NET 8, WPF, PNG, Windows ICO

---

### Task 1: Generate and package icon

- [ ] Generate the approved blue-and-gold hammer icon with transparent canvas edges.
- [ ] Copy the source PNG to `src/ToolsBox.App/Assets/baoge-toolbox-icon.png`.
- [ ] Convert it to `src/ToolsBox.App/Assets/baoge-toolbox.ico` with 16/32/48/64/128/256 layers.
- [ ] Inspect the generated asset and validate the ICO layers.

### Task 2: Apply application branding

- [ ] Set application icon, assembly name, title, product, and description in `ToolsBox.App.csproj`.
- [ ] Change the main window and sidebar labels to 宝哥工具箱.
- [ ] Preserve the existing `ToolsBox.App` root namespace.

### Task 3: Verify and document

- [ ] Update README with the displayed name.
- [ ] Run all Release tests.
- [ ] Build with zero warnings and errors.
- [ ] Confirm `宝哥工具箱.exe` exists and remains running during a startup smoke check.
