# Phase 0 Research: DLSS Version Toolkit

**Branch**: `001-dlss-version-checker` | **Date**: 2026-04-20
**Input**: `/speckit.plan` command - Technical research for implementation planning.

---

## Research 1: PowerShell Module Structure Best Practices

**Decision**: Use explicit `.psm1` module with `Export-ModuleMember` and `.psd1` manifest with proper metadata fields.

**Rationale**: PowerShell module structure (`.psm1` + `.psd1`) enables:
- `Import-Module DLSSVersion` for session import
- `Install-Module DLSSVersion` for PowerShell Gallery distribution
- Proper cmdlet export via `Export-ModuleMember` (explicit is better than implicit)
- Get-Help integration for `Get-Help Get-DLSSVersions`
- Version tracking via module manifest

The existing `check-dlss-versions.ps1` serves as standalone entry point that imports the module, preserving zero-setup usage.

**Alternatives Considered**:
1. *Single .ps1 file*: Would block PowerShell Gallery distribution, no `Export-ModuleMember`, no version metadata, no Get-Help.
2. *Autoload via Functions folder*: More complex, not needed for <300 LOC tool.
3. *Nested module structure*: Overkill for single-module project.

---

## Research 2: PS5.1 Compatibility

**Decision**: Use PS5.1-compatible syntax only: avoid ternary `?:`, null-coalescing `??`, pipe chain operators `&&`/`||`.

**Rationale**: Per Constitution Principle III (Windows-First), tool must run on Windows PowerShell 5.1 which ships with every Windows 10/11 installation. PS7+ syntax would require users to install PowerShell 7 separately, breaking zero-setup experience.

PS5.1-compatible patterns:
- Use `if/else` instead of ternary: `(condition) ? $trueVal : $falseVal` -> `if (condition) { $trueVal } else { $falseVal }`
- Use `-eq $null` or explicit check instead of null-coalescing
- Use `-and` `-or` instead of `&&`/`||`
- Use `[version]::Parse()` instead of `-split` with `-replace`
- Use `Get-Content -Raw` for single-string reads

**Alternatives Considered**:
1. *PS7+ required*: Would add installation barrier, violate Zero Dependencies and Simplicity.
2. *Feature detection*: Runtime version check with fallback adds complexity, not worth it for a small tool.
3. *Conditional syntax*: Would still fail on PS5.1, just with less clear error.

---

## Research 3: Backup Strategy for Safe Upgrades

**Decision**: Create timestamped backup of Release folder before any writes during upgrade. Backup format: `C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions Backup-{timestamp}\`. Restore from backup on any failure.

**Rationale**: Per Constitution Principle II (Safe by Default), upgrade must be safe and reversible. The backup strategy:
1. Check if backup already exists for today — skip if upgrade was just performed
2. Copy entire Release version folder to `Backup-{timestamp}` subfolder before copying new files
3. Perform upgrade operations (DLL copy, config update)
4. If any step fails: log error, leave backup in place for manual restore, exit with error code

What to backup:
- All files in the Release version folder (nvngx_*.dll, nvngx_package_config.txt)
- Subdirectory structure is preserved in backup

**Alternatives Considered**:
1. *No backup*: TOO RISKY — NVIDIA files could be corrupted, no way to restore.
2. *Backup to temp folder*: Less useful for manual restore if user needs fallback later.
3. *Shadow copy*: Requires admin, adds complexity, not needed for local tool.
4. *Mirror imaging*: Could create full image, but storage-heavy for frequent upgrades.

---

## Research 4: Encoding Handling on Windows

**Decision**: Use `-Encoding UTF8` for file writes, `-Raw` flag for file reads to get full content as single string. Account for console output cp1252 limitations.

**Rationale**: Windows file encoding can cause corruption:
- File writes without `-Encoding UTF8` may use system default (often cp1252 on Windows)
- File reads without `-Raw` may split content at newlines inconsistently
- Console output in default Windows Terminal/ConHost may render UTF-8 differently

Specific handling:
- `Get-Content -Path $file -Raw` to get config content as single string (preserves version patterns)
- `Set-Content -Path $file -Value $content -Encoding UTF8` for writes (ensures UTF8)
- `[System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)` for more control if needed
- Console output: use `Write-Host` (handles basic output), no special encoding needed for simple version display

**Alternatives Considered**:
1. *Default encoding*: May corrupt config files on non-English Windows (cp1252 default causes version string issues).
2. *Binary read*: Too complex for simple config parsing.
3. *Third-party encoding library*: Violates Zero Dependencies.

---

## Research 5: Distribution Packaging

**Decision**: Support three distribution channels with these formats:
1. **PowerShell Gallery** (primary): `.psd1` manifest with required fields
2. **Scoop**: JSON manifest (scoop manifest format)
3. **winget**: YAML file (winget YAML format)

**Rationale**: Per Constitution Principle IV (Distributable as Module), the tool must support standard Windows package managers:

PowerShell Gallery (primary):
- Uses `.psd1` manifest with fields: RootModule, ModuleVersion, GUID, Author, Description, PowerShellVersion
- Publish via `Publish-Module -Path <path> -NuGetApiKey <key>`
- Install via `Install-Module DLSSVersion -Scope CurrentUser`

Scoop (secondary, popular with power users):
- JSON manifest with: version, description, homepage, license, url, hash, checkver, ps1_update
- Install to `$ scoop bucket add ngc`, then `scoop install dlss-version-toolkit`

winget (tertiary, Microsoft-backed):
- YAML with: Id, Version, Publisher, License, InstallerType, Installers, Files
- Add to winget-pkgs repo via PR

**Alternatives Considered**:
1. *PowerShell Gallery only*: Would miss users who prefer Scoop or winget.
2. *Chocolatey*: Less common in gaming communities, more enterprise-focused.
3. *Direct download*: No package management, harder to update.

---

## Research 6: Pester Test Patterns for Filesystem-Dependent Code

**Decision**: Use Pester 5.x for testing, mock NVIDIA NGX paths with `Mock -MockWith` to avoid dependency on actual NVIDIA installations. Provide sample test patterns.

**Rationale**: Filesystem-dependent code is hard to test because:
- Requires actual NVIDIA NGX folder structure on test machine
- May fail on machines without NVIDIA software
- Can't test edge cases (missing folders, corrupted configs) easily

Solution: Mock filesystem operations:
```powershell
Mock Get-ChildItem -MockWith { # return test objects }
Mock Get-Content -MockWith { "dlss, 310.6.0.0`ndlssg, 310.6.0.0" }
Mock Test-Path -MockWith { $true }
```

Test patterns to include:
1. *Happy path*: Mocked NGX folders with valid configs
2. *Missing folders*: Mock Test-Path to return $false
3. *Corrupted config*: Mock invalid config content
4. *Permission error*: Mock to throw termination error
5. *Version comparison*: Test semantic version sorting

**Alternatives Considered**:
1. *Integration tests only*: Would require real NVIDIA installation on CI, flaky.
2. *No tests*: Would violate Safe by Default, regression risk too high.
3. *Test with temp folders*: More realistic but more setup, easier to skip test runs.

---

## Post-Design Constitution Check

| Principle | Status | Justification |
|-----------|--------|---------------|
| **Zero Dependencies** | PASS | Pure PowerShell 5.1. Pester is dev-only, not bundled with tool. |
| **Safe by Default** | PASS | Explicit backup before upgrade, restore on failure, read-only default. |
| **Windows-First** | PASS | PS5.1 syntax, UTF8 encoding, Windows paths, no cross-platform code. |
| **Distributable as Module** | PASS | Module structure supports Gallery/Scoop/winget. Standalone entry point preserved. |
| **Simplicity Over Flexibility** | PASS | Single-purpose: inspect and upgrade DLSS. No extra features. |