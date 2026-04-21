# Implementation Plan: DLSS Version Toolkit

**Branch**: `001-dlss-version-checker` | **Date**: 2026-04-20 | **Spec**: [spec.md](./spec.md)

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

The DLSS Version Toolkit is a Windows PowerShell CLI tool that enables PC gamers to check installed NVIDIA DLSS versions (Release and Staging) and optionally upgrade their Release DLSS to the latest Staging version. The tool scans NVIDIA NGX folder structures, parses version config files, and provides upgrade functionality by copying staging DLLs to the release location. The project also includes packaging for distribution via PowerShell Gallery, Scoop, and winget.

## Technical Context

**Language/Version**: PowerShell 5.1+ (compatible with Windows PowerShell 5.1 and PowerShell 7+)  
**Primary Dependencies**: None (pure PowerShell, no external modules required)  
**Storage**: Local file system only - reads from NVIDIA NGX folders  
**Testing**: Pester (PowerShell testing framework)  
**Target Platform**: Windows 10/11 (x64)  
**Project Type**: CLI tool / PowerShell Module  
**Performance Goals**: Execute version scan in under 5 seconds  
**Constraints**: Must work without administrative elevation for read operations; upgrade requires write access to ProgramData  
**Scale/Scope**: Single-user local tool, approximately 200-300 lines of code expected

## Constitution Check

The following principles guide this implementation:

| Principle | Application |
|-----------|-------------|
| **Simplicity** | Tool does one thing well - check and upgrade DLSS versions. No unnecessary features. |
| **Windows-First** | Native PowerShell solution optimized for Windows 10/11. No cross-platform complexity. |
| **No Dependencies** | Pure PowerShell 5.1 with no external module requirements. Works offline. |
| **Safe Upgrades** | Upgrade only copies files, never deletes. Clear feedback on what changed. |

**GATE**: Must pass before Phase 0 research. Re-check after Phase 1 design.

The implementation uses only standard PowerShell cmdlets and .NET types already available in Windows, ensuring maximum compatibility.

## Project Structure

### Documentation (this feature)

```text
specs/001-dlss-version-checker/
├── plan.md              # This file
├── spec.md              # Feature specification
├── tasks.md             # Task breakdown
└── research.md          # Phase 0 output (if needed)
```

### Source Code (repository root)

```text
C:\Users\jolti.PHANERON\dlss-version-toolkit/
├── check-dlss-versions.ps1    # Main script (existing)
├── src/
│   ├── DLSSVersion.psm1       # Module (refactored from script)
│   └── DLSSVersion.psd1       # Module manifest
├── tests/
│   └── DLSSVersion.Tests.ps1  # Pester tests
├── specs/
│   └── 001-dlss-version-checker/
│       ├── spec.md
│       ├── plan.md
│       └── tasks.md
└── README.md                   # Documentation
```

**Structure Decision**: The project follows a single-project structure with `src/` containing the PowerShell module files and `tests/` containing Pester tests. The existing `check-dlss-versions.ps1` script will be preserved for standalone usage while a proper PowerShell module is created for distribution. This dual approach supports both direct script execution and proper module installation via package managers.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| PowerShell Module | Required for PowerShell Gallery distribution and proper cmdlet export | Script-only approach would work but lacks proper module structure for distribution |
| Pester Tests | Ensures reliability for a tool that modifies system files | Manual testing possible but risks regressions |

No major complexity violations are anticipated. The tool uses a straightforward file-copy approach for upgrades.

## Implementation Phases

### Phase 1: Module Refactoring
- Refactor existing script into proper PowerShell module (.psm1)
- Create module manifest (.psd1) with proper metadata
- Ensure backward compatibility with existing script

### Phase 2: Testing Infrastructure
- Set up Pester test framework
- Write unit tests for version parsing functions
- Write integration tests for file system operations

### Phase 3: Distribution Packaging
- Create PowerShell Gallery package
- Create Scoop bucket manifest
- Create winget manifest

### Phase 4: Documentation
- Write README with usage instructions
- Document all cmdlets with examples
- Create quickstart guide
