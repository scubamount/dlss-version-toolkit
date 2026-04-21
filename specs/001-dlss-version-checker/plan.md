# Implementation Plan: DLSS Version Toolkit

**Branch**: `001-dlss-version-checker` | **Date**: 2026-04-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification: Windows tool for checking and upgrading NVIDIA DLSS versions on a user's system with PowerShell module distribution.

## Summary

A PowerShell CLI tool and module for inspecting and upgrading NVIDIA DLSS override versions. Reads NVIDIA NGX folder structure (Release and Staging locations), parses `nvngx_package_config.txt` for component versions, and optionally upgrades Release DLSS to match the latest Staging version. Technical approach: PowerShell 5.1-compatible module with standalone script entry point, read-only by default with explicit upgrade safety via backup/restore.

## Technical Context

**Language/Version**: PowerShell 5.1+ (Windows PowerShell, PS5.1 baseline, compatible with PS7)
**Primary Dependencies**: None (pure PowerShell, no external modules, Pester dev-only for testing)
**Storage**: Local file system (NVIDIA NGX folders at C:\ProgramData\NVIDIA\NGX)
**Testing**: Pester (PowerShell testing framework, dev-only dependency, not bundled with tool)
**Target Platform**: Windows 10 version 2004+ / Windows 11 x64
**Project Type**: CLI tool / PowerShell Module (DLSSVersion.psm1 + check-dlss-versions.ps1 entry point)
**Performance Goals**: Version scan in under 5 seconds
**Constraints**: No admin elevation for read operations; upgrade needs write access to ProgramData; no PS7+ syntax (no ternary, no null-coalescing, no &&/||); UTF8 encoding for file I/O
**Scale/Scope**: ~200-300 LOC, single-user local tool

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Justification |
|-----------|--------|---------------|
| **Zero Dependencies** | PASS | Pure PowerShell 5.1 with no external modules or runtimes. Pester is dev-only, not bundled. |
| **Safe by Default** | PASS | Default invocation is read-only (check only). Upgrade requires explicit -Upgrade flag. Backup created before any writes, restore on failure. |
| **Windows-First** | PASS | Uses Windows paths, PS5.1-compatible syntax only, UTF8 encoding with -Raw flag for file reads. No cross-platform abstractions. |
| **Distributable as Module** | PASS | Structured as PowerShell module (DLSSVersion.psm1 + DLSSVersion.psd1). Standalone script preserved for zero-setup execution. Supports PowerShell Gallery, Scoop, winget. |
| **Simplicity Over Flexibility** | PASS | Single-purpose tool: inspect and upgrade DLSS versions. No game detection, driver management, or NVIDIA App integration. |

## Project Structure

### Documentation (this feature)

```text
specs/001-dlss-version-checker/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── cli-interface.md
│   └── module-interface.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created here)
```

### Source Code (repository root)

```text
dlss-version-toolkit/
├── check-dlss-versions.ps1  # Standalone script entry point (preserves zero-setup usage)
├── src/
│   ├── DLSSVersion.psm1     # Module implementation (core functions)
│   └── DLSSVersion.psd1     # Module manifest (metadata for PowerShell Gallery)
├── tests/
│   └── DLSSVersion.Tests.ps1  # Pester tests (dev-only)
└── README.md
```

**Structure Decision**: Chose PowerShell Module structure per Constitution Principle IV (Distributable as Module). The `src/` directory contains the module files (DLSSVersion.psm1 + .psd1) which enables `Import-Module DLSSVersion` and PowerShell Gallery publishing. The standalone script (`check-dlss-versions.ps1`) remains at root as a convenience entry point for users who prefer direct execution without module installation — preserving the zero-setup experience while meeting module distribution requirements.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| PowerShell Module structure adds file separation (.psm1 + .psd1 + standalone script) | Required by Constitution Principle IV - Distributable as Module for PowerShell Gallery publishing. Module structure enables `Install-Module DLSSVersion`, proper cmdlet naming via verb-noun, and Get-Help integration. | Simpler: Single .ps1 file. REJECTED: Would block PowerShell Gallery distribution, no proper Export-ModuleMember, no Get-Help integration, no version tracking. Module structure is minimal overhead (~5 lines) for significant distribution capability. |

## Post-Design Constitution Check

*Re-evaluation after Phase 1 design completed. References specific design decisions from research.md, data-model.md, and contracts/.*

| Principle | Status | Justification |
|-----------|--------|---------------|
| **Zero Dependencies** | PASS | Pure PowerShell 5.1 with no external modules or runtimes. Pester is dev-only (not bundled, not required for end-user execution). The `-Path` parameter on module functions uses only built-in PowerShell types (string). No .NET SDK, no Python, no Node.js required. |
| **Safe by Default** | PASS | Default invocation is read-only (check only). Upgrade requires explicit `-Upgrade` flag (CLI) or `Start-DLSSUpgrade` call (module). Backup strategy: timestamped `.dlss-backup-<yyyyMMdd-HHmmss>` folder created before any writes, with automatic rollback on failure and manual restore path if rollback also fails. `Start-DLSSUpgrade` supports `-WhatIf` for dry-run and `-Confirm` for interactive gate. Exit code 1 on any error. |
| **Windows-First** | PASS | All paths use Windows conventions (`C:\ProgramData\NVIDIA\NGX\...`). PS5.1-compatible syntax enforced: no ternary `?:`, no null-coalescing `??`, no chain operators `&&`/`||`. File reads use `Get-Content -Raw` to handle encoding correctly. File copies during upgrade use `Copy-Item` (preserves original encoding byte-for-byte, avoids PS5.1 UTF-16LE default on `Set-Content`). No cross-platform abstractions. |
| **Distributable as Module** | PASS | Structured as PowerShell module (`DLSSVersion.psm1` + `DLSSVersion.psd1`) with explicit `Export-ModuleMember` for three functions. Manifest includes all required PowerShell Gallery fields (GUID, ModuleVersion, Author, Description, PowerShellVersion, FunctionsToExport). Standalone script (`check-dlss-versions.ps1`) preserved as thin wrapper (~20 LOC) that imports the module. Distribution channels documented: PowerShell Gallery (primary), Scoop (secondary), winget (tertiary). |
| **Simplicity Over Flexibility** | PASS | Single-purpose tool: inspect and upgrade NVIDIA DLSS override versions. Three exported functions only (Get-DLSSVersions, Get-DLSSLatestVersion, Start-DLSSUpgrade). No game detection, no driver management, no NVIDIA App integration, no download capability. ~200-300 LOC. The `-Path` parameter is the only extensibility point, justified solely for testability (not a feature extension). Module structure is minimal overhead per Complexity Tracking table. |

### Design Decisions Validated by Constitution

1. **Backup format `.dlss-backup-<timestamp>`**: Chosen over temp directory backup because it stays within the same ACL context (Constitution Principle II: Safe by Default -- backup must be accessible for manual restore).
2. **`Copy-Item` over `Get-Content | Set-Content`**: Chosen for upgrade file copies to preserve original encoding (Constitution Principle III: Windows-First -- avoids PS5.1 UTF-16LE encoding corruption).
3. **`$TestDrive` fixtures over cmdlet mocks**: Chosen for test isolation because it tests real filesystem interaction without requiring NVIDIA software (Constitution Principle I: Zero Dependencies -- Pester is dev-only, tests must run on any machine).
4. **Three distribution channels (Gallery/Scoop/winget)**: Chosen to cover the Windows audience without adding Chocolatey (Constitution Principle V: Simplicity Over Flexibility -- three channels are sufficient, four would be scope creep).
5. **`-Path` parameter default to real NGX path**: Chosen over environment variable override because it is scoped per call, avoiding global state leakage between tests (Constitution Principle V: Simplicity -- parameter injection is simpler than environment-based configuration).