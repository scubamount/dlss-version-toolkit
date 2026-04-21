<!--
Sync Impact Report
==================
Version change: N/A → 1.0.0 (initial ratification)
Modified principles: N/A (first fill from template)
Added sections:
  - I. Zero Dependencies
  - II. Safe by Default
  - III. Windows-First
  - IV. Distributable as a Module
  - V. Simplicity Over Flexibility
  - Platform Constraints
  - Development Workflow
  - Governance
Removed sections: None (template placeholders replaced)
Templates requiring updates:
  - .specify/templates/plan-template.md: ✅ compatible (Constitution Check section aligns)
  - .specify/templates/spec-template.md: ✅ compatible (requirements format supports principles)
  - .specify/templates/tasks-template.md: ✅ compatible (task phases align with workflow)
Follow-up TODOs: None
-->

# DLSS Version Toolkit Constitution

## Core Principles

### I. Zero Dependencies

The tool MUST run on any Windows 10/11 system with PowerShell 5.1+ pre-installed. No Python, Node.js, .NET SDK, or external runtimes required. The only acceptable dependencies are Windows built-in cmdlets and optional Pester for testing during development only.

**Rationale**: PC gamers are not developers. Requiring runtime installations creates an immediate adoption barrier. PowerShell 5.1 ships with every Windows 10/11 installation — zero setup means zero friction.

### II. Safe by Default

All mutating operations (DLL copy, config overwrite) require an explicit `-Upgrade` flag. The default invocation MUST be read-only and side-effect-free. Upgrade operations MUST create a backup of the release folder before any writes. If any step fails mid-upgrade, the tool MUST restore from backup.

**Rationale**: Users run this tool against system-level NVIDIA files. A mistake could break DLSS for all games. Safety defaults prevent accidental damage and make the tool trustworthy.

### III. Windows-First

Target platform is Windows 10/11 with PowerShell 5.1+. Do not use PowerShell 7+ features: no ternary `? :`, no null-coalescing `??`, no pipe chain operators `&&`/`||`. All paths MUST use Windows conventions. Encoding issues MUST be handled explicitly — use `-Encoding UTF8` or `-Raw` for file reads, and account for cp1252 console output limitations.

**Rationale**: Cross-platform abstractions add complexity for zero benefit — this tool will never run on Linux or macOS because NVIDIA NGX only exists on Windows. PowerShell 5.1 compatibility ensures it works on every Windows machine without installing PowerShell 7.

### IV. Distributable as a Module

The tool MUST be structured as a PowerShell module (`DLSSVersion.psm1` + `.psd1`) for PowerShell Gallery publishing. The standalone script (`check-dlss-versions.ps1`) MUST remain as a convenience entry point that imports the module. Scoop and winget packaging are secondary distribution channels.

**Rationale**: PowerShell Gallery is the standard Windows package manager for scripts. Module structure enables `Install-Module DLSSVersion`, proper cmdlet naming, and Get-Help integration. The standalone script preserves the zero-setup experience for users who prefer direct execution.

### V. Simplicity Over Flexibility

YAGNI. The tool does one thing well: inspect and upgrade NVIDIA DLSS override versions. Do not add game-detection, driver management, or NVIDIA App integration. The codebase MUST stay small enough for a single contributor to maintain.

**Rationale**: Scope creep kills small tools. Every feature added doubles the maintenance surface. This tool solves one specific problem — keeping DLSS versions current — and should resist feature creep aggressively.

## Platform Constraints

- **Runtime**: PowerShell 5.1+ (Windows PowerShell) — no PowerShell 7 requirement
- **OS**: Windows 10 version 2004+ or Windows 11 (required for NVIDIA DLSS override feature)
- **Privileges**: Standard user for reading; Admin for writing to `C:\ProgramData\`
- **NVIDIA**: Requires NVIDIA App with DLSS override enabled; tool reads NGX model directories
- **File Encoding**: All file I/O MUST use `-Encoding UTF8` or `-Raw` to avoid cp1252 corruption
- **Console Output**: MUST work in Windows Terminal, ConHost, and VS Code integrated terminal

## Development Workflow

- **Testing**: Pester tests for all functions; manual smoke test on real NVIDIA NGX directories
- **Versioning**: Semantic versioning (MAJOR.MINOR.PATCH) aligned with PowerShell Gallery best practices
- **Releases**: Git tag → PowerShell Gallery publish → Scoop manifest update → winget PR
- **Code Review**: Any change to upgrade logic MUST be tested against a real DLSS override directory structure
- **Breaking Changes**: Bump MAJOR version; provide migration guide in release notes

## Governance

This constitution supersedes all other practices and conventions. Any deviation MUST be justified with:

1. The specific problem that requires the deviation
2. Why following the principle would cause user harm or tool failure
3. A plan to return to compliance

Amendments require documentation of the change, rationale, and impact analysis. All PRs and reviews MUST verify compliance with these principles. Complexity that violates the Simplicity principle MUST be explicitly justified.

**Version**: 1.0.0 | **Ratified**: 2026-04-20 | **Last Amended**: 2026-04-20
