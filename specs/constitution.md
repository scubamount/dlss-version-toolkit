# DLSS Version Toolkit Constitution

## Core Principles

### I. Zero Dependencies

The tool MUST run on any Windows 10/11 system with PowerShell 5.1+ pre-installed. No Python, Node.js, .NET SDK, or external runtimes required. The only acceptable dependencies are Windows built-in cmdlets and optional Pester for testing during development only.

### II. Safe by Default

All mutating operations (DLL copy, config overwrite) require an explicit `-Upgrade` flag. The default invocation is read-only and side-effect-free. Upgrade operations MUST create a backup of the release folder before any writes. If any step fails mid-upgrade, the tool MUST restore from backup.

### III. Windows-First

Target platform is Windows 10/11 with PowerShell 5.1+. Do not use PowerShell 7+ features (ternary `? :`, null-coalescing `??`, pipe chain operators `&&`/`||`). All paths use Windows conventions. Encoding issues MUST be handled explicitly (UTF-8 for config reads, cp1252 awareness for console output).

### IV. Distributable as a Module

The tool MUST be structured as a PowerShell module (`DLSSVersion.psm1` + `.psd1`) for PowerShell Gallery publishing. The standalone script (`check-dlss-versions.ps1`) remains as a convenience entry point that imports the module. Scoop and winget packaging are secondary distribution channels.

### V. Simplicity Over Flexibility

YAGNI. The tool does one thing well: inspect and upgrade NVIDIA DLSS override versions. Do not add game-detection, driver management, or NVIDIA App integration. Keep the scope narrow and the codebase small enough for a single contributor to maintain.

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

Amendments require documentation of the change, rationale, and impact analysis.

**Version**: 1.0.0 | **Ratified**: 2026-04-20 | **Last Amended**: 2026-04-20
