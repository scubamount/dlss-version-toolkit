# DLSS Version Toolkit v1.0 Refactor Plan

## Overview
Transform the DLSS Version Toolkit from a developer-oriented PowerShell prototype into a polished, safe, end-user-friendly Windows utility.

## Guiding Principles
1. **Read-only first** — Default/first-run experience never mutates system files
2. **Explicit confirmation** — No write operation without human-readable confirmation or `-Yes`
3. **No NGX internals required** — End users get value without understanding NGX
4. **Progressive detail** — Summary first, tables second, verbose/diagnostic on request
5. **Safe writes** — Backup, verify, log, reversible for every write operation
6. **Windows-native feel** — .cmd launcher, Start Menu shortcut, clean console output

## Key Decisions
- **GUI deferred to v2.0** — 500-800 lines of WinForms is scope explosion; CLI + .cmd launcher sufficient
- **Legacy flags preserved** — `-Upgrade`, `-Compare`, `-Sync`, `-All` still work but emit deprecation warning
- **Backward-compatible API** — Old module function names kept as aliases for one major version
- **PS5.1 baseline** — No `??`, `?:`, `foreach-parallel`, or PS7-only cmdlets
- **ConvertTo-Json -Depth 10** — PS5.1 defaults to depth 2, must override

---

## Phase 1: Internal Refactor — Separate Core Logic from Rendering

**Goal**: Pure functions return objects; Write-Host removed from module; no public API change yet.

### 1.1 Module decomposition
Split `DLSSVersion.psm1` (1531 lines) into focused sub-modules:

| New File | Responsibility | Lines (est.) |
|----------|---------------|--------------|
| `src/DLSSVersion/Core.psm1` | Version comparison, config parsing, DLL reading | ~350 |
| `src/DLSSVersion/Discovery.psm1` | Scan NGX Release/Staging/Streamline/AnWave | ~300 |
| `src/DLSSVersion/Plan.psm1` | Generate update plans, recommendations | ~200 |
| `src/DLSSVersion/Execution.psm1` | Backup, copy, verify, rollback | ~350 |
| `src/DLSSVersion/Restore.psm1` | List backups, restore by ID/path/latest | ~150 |
| `src/DLSSVersion/Diagnostics.psm1` | Doctor checks, system readiness | ~150 |
| `src/DLSSVersion/Logging.psm1` | Structured log writes, manifest JSON | ~100 |
| `src/DLSSVersion/Rendering.psm1` | Console output, tables, colors, JSON formatter | ~200 |

The main `DLSSVersion.psm1` becomes a thin orchestrator that dot-sources these sub-modules and exports the unified API.

### 1.2 Extract Write-Host from core
- 94 Write-* calls in module → move all to Rendering.psm1 or CLI layer
- Core functions return structured result objects (PSCustomObject)
- Private functions use `Write-Verbose` for diagnostic output (already pipeline-safe)
- `Write-Warning` for genuine issues → kept but wrapped in configurable logger
- `Write-Error` → replaced with `throw` or error-result objects

### 1.3 Pure function extraction
- `Test-VersionNewer` → already pure, keep
- `Get-NgxVersionConfig` → return config object instead of writing warnings
- `Get-GlobalDllVersions` → return version objects, log issues via logger
- `New-DLSSBackup` → return backup-result object (success/fail/path/fileCount)
- `Restore-DLSSBackup` → return restore-result object

### 1.4 Fix existing bugs
- `$stagingFolder` never assigned in `Start-DLSSUpgrade` (line ~1056)
- Backup on failed rollback leaves indeterminate state → add explicit "partially failed" status
- `ConvertTo-Json -Depth` not specified anywhere → add `-Depth 10`

### Testing
- All 24 existing tests must continue to pass
- Add tests for new return types (objects, not console output)
- Test that module functions produce no Write-Host output (capture stream)

---

## Phase 2: New CLI + Legacy Shim

**Goal**: `dlss-toolkit.ps1` with subcommands; old `check-dlss-versions.ps1` becomes a thin shim.

### 2.1 Command surface
```
dlss-toolkit.ps1 scan          # Read-only: system readiness + detected versions
dlss-toolkit.ps1 compare       # Read-only: cross-source comparison table
dlss-toolkit.ps1 plan          # Read-only: proposed file copies with before/after
dlss-toolkit.ps1 apply         # Write: requires confirmation + elevation + backup
dlss-toolkit.ps1 restore       # Write: restore from backup (--latest, --id, --path)
dlss-toolkit.ps1 doctor        # Read-only: system diagnostics
dlss-toolkit.ps1 logs          # Read-only: open/log log directory
dlss-toolkit.ps1 help          # Read-only: usage info
```

### 2.2 Legacy flag mapping
| Legacy Flag | New Equivalent | Behavior Change |
|-------------|---------------|-----------------|
| (none) | `scan` | Identical — read-only |
| `-Compare` | `compare` | Identical — read-only |
| `-Upgrade` | `plan` then `apply` | Now requires confirmation (was auto-apply) |
| `-Sync` | `plan` then `apply` | Now requires confirmation |
| `-All` | `plan` | **Changed**: Was auto-apply, now read-only preview. Must pass `-Yes` for auto-apply |
| `-GlobalPath "X"` | `-GlobalPath "X"` | Identical |
| `-StreamlinePath "X"` | `-StreamlinePath "X"` | Identical |

When legacy mutation flags are used, emit: `WARNING: -Upgrade/-Sync/-All will change behavior in v1.0. Use 'plan' to preview, 'apply' to execute. Pass -Yes to skip this warning.`

### 2.3 New flags
| Flag | Applies To | Purpose |
|------|-----------|---------|
| `-WhatIf` | apply, restore | Preview without executing |
| `-Confirm` | apply, restore | Prompt before each operation |
| `-Yes` | apply, restore | Skip confirmation prompts |
| `-NoColor` | all | Disable ANSI color codes |
| `-Json` | scan, compare, plan, doctor | Machine-readable JSON output |
| `-LogPath "X"` | all | Custom log file path |
| `-Verbose` | all | Show detailed detection logic |
| `-Quiet` | all | Suppress all non-error output |
| `-OpenLog` | logs | Open log file in editor |
| `-RestoreLatest` | restore | Restore most recent backup |
| `-BackupRoot "X"` | apply, restore | Custom backup directory |

### 2.4 Command routing implementation
```powershell
param(
    [Parameter(Position=0)]
    [ValidateSet('scan','compare','plan','apply','restore','doctor','logs','help')]
    [string]$Command = 'scan',
    # ... other params
)
switch ($Command) {
    'scan'    { Invoke-DLSSScan @boundParams }
    'compare' { Invoke-DLSSCompare @boundParams }
    'plan'    { Invoke-DLSSPlan @boundParams }
    'apply'   { Invoke-DLSSApply @boundParams }
    'restore' { Invoke-DLSSRestore @boundParams }
    'doctor'  { Invoke-DLSSDoctor @boundParams }
    'logs'    { Invoke-DLSSLogs @boundParams }
    'help'    { Show-DLSSHelp }
}
```

### 2.5 Legacy shim
`check-dlss-versions.ps1` → thin wrapper that maps old flags to new commands and emits deprecation warning.

### Testing
- Default `dlss-toolkit.ps1` (no args) = `scan` → must be read-only
- `-All` without `-Yes` → shows plan, does NOT apply
- Legacy flags emit deprecation warning
- `-Json` output is valid JSON, no ANSI codes
- `-NoColor` suppresses all `[3xm` sequences

---

## Phase 3: Safety, Logging, and Error Messages

**Goal**: Every write operation has backup+manifest+rollback; structured logging; actionable error messages.

### 3.1 Logging infrastructure
- Default log directory: `%LOCALAPPDATA%\DLSSVersionToolkit\Logs`
- Operation manifests: `%LOCALAPPDATA%\DLSSVersionToolkit\Operations`
- Every run writes a timestamped log file
- Log format: `[2026-05-03 22:00:00] [INFO] Message`
- `-Verbose` enables debug-level logging
- `-Quiet` suppresses console output but still writes log file

### 3.2 Operation manifest JSON
Written after every `apply` operation:
```json
{
    "operationId": "guid",
    "timestamp": "ISO8601",
    "toolVersion": "1.0.0",
    "osVersion": "10.0.19045",
    "psVersion": "5.1.19041",
    "elevated": true,
    "source": { "type": "Staging", "path": "C:\\...", "versions": {} },
    "target": { "type": "Release", "path": "C:\\...", "versions": {} },
    "backup": { "path": "%LOCALAPPDATA%\\...\\Backups\\...", "fileCount": 4, "verified": true },
    "files": [
        { "name": "nvngx_dlss.dll", "sourceHash": "sha256:...", "targetHash": "sha256:..." }
    ],
    "result": { "status": "success|failed|rolledBack|partialFailure", "message": "" }
}
```

### 3.3 Backup system overhaul
- Backups stored in: `%LOCALAPPDATA%\DLSSVersionToolkit\Backups\<operationId>\`
- Each backup includes: copied files + source manifest JSON
- Backup verification: file count + SHA256 hash of each file
- On `apply` failure: automatic rollback attempt, report rollback status
- On rollback failure: explicit "partially failed" status with recovery instructions

### 3.4 Elevation handling
```powershell
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and $Command -eq 'apply') {
    Write-Host "Apply needs administrator rights because it writes to C:\ProgramData\NVIDIA\NGX."
    Write-Host "No changes were made."
    Write-Host "Re-run from an elevated PowerShell:"
    Write-Host "  Start-Process powershell -Verb RunAs -ArgumentList '-ExecutionPolicy Bypass -File `"$PSCommandPath`" apply -Yes'"
    exit 1
}
```

### 3.5 Error message standard
Every user-facing error includes:
1. **What failed**: "Cannot write to C:\ProgramData\NVIDIA\NGX\..."
2. **Why it likely failed**: "This folder requires administrator permissions."
3. **Whether anything changed**: "No changes were made."
4. **What to do next**: "Re-run from an elevated PowerShell: <exact command>"
5. **Log file path**: "See log: %LOCALAPPDATA%\DLSSVersionToolkit\Logs\20260503-220000.log"

### Testing
- `plan` never writes files (verify with file-system audit)
- `apply` refuses without confirmation unless `-Yes`
- `apply` refuses without elevation for protected paths
- Backup created before writes, verified with hash
- Rollback attempted on copy failure
- Manifest JSON is valid, contains all required fields
- Legacy `-All` without `-Yes` does NOT mutate

---

## Phase 4: Doctor + Restore UX

**Goal**: `doctor` for diagnostics; `restore` for rollback.

### 4.1 Doctor command
Checks:
1. PowerShell version (5.1+ required)
2. OS version (Windows 10 2004+)
3. NVIDIA NGX path existence
4. Write permission for apply targets
5. Whether running elevated
6. Long-path support enabled
7. Execution policy
8. Expected DLL/config files exist
9. Optional source detection (Streamline SDK, AnWave)
10. Free disk space for backups

Output format:
```
DLSS Version Toolkit — Doctor
─────────────────────────────
[OK] PowerShell 5.1.19041.1
[OK] Windows 10.0.19045
[OK] NVIDIA NGX folder found
[OK] DLSS override enabled (2 versions)
[OK] Long-path support enabled
[WARN] Not running as Administrator (needed for apply)
[INFO] Streamline SDK: not detected (optional — download from developer.nvidia.com)
[INFO] AnWave/dlssglom: not detected (optional — download from github.com/cybertron010/dlssglom)

Run 'scan' to check versions, or 'apply' (as admin) to update.
```

### 4.2 Restore command
- `restore` (no args) → list available backups with timestamp, target, operation ID
- `restore --latest` → restore most recent backup
- `restore --id <operationId>` → restore specific backup
- `restore --backup-path <path>` → restore from custom path
- Requires confirmation unless `-Yes`
- Verifies restored files after copy
- Scans both new backup location (`%LOCALAPPDATA%\...\Backups\`) and legacy location (NGX `.dlss-backup-*`)

### 4.3 First-run summary
`scan` output:
```
DLSS Version Toolkit v1.0
─────────────────────────────────────────
NVIDIA NGX: Found
DLSS Override: Enabled (2 versions installed)
Admin: Not required for scan
Sources detected: NGX Release, NGX Staging, Streamline SDK, AnWave/dlssglom

Your active DLSS override (310.6.0.0) appears current.
  → No updates needed.

OR

A newer version appears available in Staging (310.7.0.0).
  → Run: .\dlss-toolkit.ps1 plan

OR

No NVIDIA NGX DLSS override folders were found.
  → Open NVIDIA App, enable DLSS override, then run: .\dlss-toolkit.ps1 scan
```

### Testing
- Doctor produces expected pass/fail for each check
- Doctor saves diagnostics to log file
- Restore lists available backups
- Restore --latest restores most recent
- Restore verifies files after copy
- Legacy backup locations are also discovered

---

## Phase 5: Packaging, Installers, and CI

**Goal**: User-friendly Windows install; GitHub Actions CI/CD.

### 5.1 install.ps1 rewrite
- Installs to per-user path (no admin needed): `$env:LOCALAPPDATA\DLSSVersionToolkit\`
- Adds Start Menu shortcut
- Optionally adds desktop shortcut
- Prints exact next command
- Validates PS5.1+, NGX existence (advisory, not blocking)

### 5.2 uninstall.ps1
- Removes installed files and shortcuts
- Offers to preserve or remove logs/backups
- Removes module from PSModulePath if installed there

### 5.3 DLSS Version Toolkit.cmd
```cmd
@echo off
title DLSS Version Toolkit
echo Running scan...
powershell -ExecutionPolicy Bypass -File "%~dp0dlss-toolkit.ps1" scan
echo.
echo Press any key to close...
pause >nul
```
For apply: relaunches elevated via `Start-Process -Verb RunAs`.

### 5.4 GitHub Actions workflow
```yaml
# .github/workflows/ci.yml
name: CI
on: [push, pull_request]
jobs:
  test:
    strategy:
      matrix:
        include:
          - os: windows-latest
            shell: powershell  # PS5.1
          - os: windows-latest
            shell: pwsh        # PS7
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - name: Install Pester
        shell: ${{ matrix.shell }}
        run: Install-Module Pester -Force -Scope CurrentUser -MinimumVersion 5.0
      - name: Run Pester tests
        shell: ${{ matrix.shell }}
        run: |
          $config = New-PesterConfiguration
          $config.Run.Path = './tests'
          $config.Run.Exit = $true
          Invoke-Pester -Configuration $config

  analyze:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: PSScriptAnalyzer
        shell: pwsh
        run: |
          Install-Module PSScriptAnalyzer -Force -Scope CurrentUser
          $results = Invoke-ScriptAnalyzer -Path ./src -Recurse -Severity Error
          if ($results) { $results | Format-Table; exit 1 }

  release:
    if: startsWith(github.ref, 'refs/tags/v')
    needs: [test, analyze]
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: Package
        shell: pwsh
        run: |
          Compress-Archive -Path src/,check-dlss-versions.ps1,dlss-toolkit.ps1,install.ps1,README.md,LICENSE -DestinationPath dlss-version-toolkit-$env:VERSION.zip
          $hash = (Get-FileHash dlss-version-toolkit-$env:VERSION.zip -Algorithm SHA256).Hash
          $hash | Out-File dlss-version-toolkit-$env:VERSION.zip.sha256
      - uses: softprops/action-gh-release@v2
        with:
          files: |
            *.zip
            *.sha256
```

### 5.5 Manifest fixes
- Winget manifest: mark as "planned" until first real release
- Scoop manifest: mark as "planned" until first real release
- Add README section: "Install: winget (planned) | Scoop (planned) | PowerShell Gallery (planned)"
- Update hashes only when release artifact exists

### 5.6 PSScriptAnalyzer settings
Create `PSSA-settings.psd1`:
```powershell
@{
    Rules = @{
        PSUseCompatibleSyntax = @{ Enable = $true; TargetVersions = @('5.1') }
        PSUseCompatibleCommands = @{ Enable = $true; TargetProfiles = @('win-48_x64_10.0.17763.0_5.1') }
        PSAvoidUsingWriteHost = @{ Enable = $true }  # Enforce no Write-Host in module
    }
}
```

### Testing
- install.ps1 creates shortcuts and prints next command
- uninstall.ps1 removes files and offers log preservation
- .cmd launcher opens PowerShell and runs scan
- CI workflow passes on both PS5.1 and PS7
- PSScriptAnalyzer finds zero errors
- Zip artifact contains all required files

---

## Phase 6: Documentation Rewrite

**Goal**: README for end users first; help system; safety promises.

### 6.1 README structure
1. **What this does** — One paragraph
2. **Is this safe?** — Scan is read-only. Apply creates backup. Restore is available. Admin only for writes.
3. **Quick start** — `.\dlss-toolkit.ps1 scan`
4. **Preview changes** — `.\dlss-toolkit.ps1 plan`
5. **Apply changes** — `.\dlss-toolkit.ps1 apply`
6. **Restore** — `.\dlss-toolkit.ps1 restore --latest`
7. **Diagnostics** — `.\dlss-toolkit.ps1 doctor`
8. **Troubleshooting** — No NGX folder, Access denied, Execution policy, Optional sources not found
9. **Advanced** — Custom paths, JSON output, Automation/-Yes, Module API
10. **Install methods** — Direct, Module, winget (planned), Scoop (planned)

### 6.2 Safety promises (prominent)
> **This tool is safe to run.** The default `scan` command is 100% read-only — it never modifies any files. The `plan` command shows what *would* change, without changing anything. The `apply` command requires explicit confirmation and creates a backup before any file is touched. If anything goes wrong, `restore` reverts to the previous state.

### 6.3 Help system
- `dlss-toolkit.ps1 help` → show all commands with descriptions
- `dlss-toolkit.ps1 help scan` → show detailed help for scan command
- Each subcommand has `.DESCRIPTION`, `.EXAMPLE` in comment-based help

### 6.4 Manifest status labels
- winget: "Planned" (not available until first tagged release)
- Scoop: "Planned"
- PowerShell Gallery: "Planned"
- Remove empty hashes from manifests, add comment "# Updated by CI on release"

### Testing
- README commands correspond to real script/functions
- Help output matches actual behavior
- No speculative install methods shown as working

---

## Acceptance Criteria (from spec)

1. ✅ Non-technical user can download, scan, understand, preview, apply safely, restore — without reading source
2. ✅ First README command is read-only
3. ✅ No "quick start" command writes to ProgramData
4. ✅ Every write operation requires confirmation or `-Yes`
5. ✅ Every write operation creates backup and operation manifest
6. ✅ Restore is documented and tested
7. ✅ Missing optional sources = optional, not failure
8. ✅ GitHub release artifacts + manifests either valid or marked "planned"
9. ✅ CI runs tests + packages release artifacts
10. ✅ README, quickstart, script help, and actual behavior match

---

## Effort Estimates

| Phase | Scope | Estimated Effort |
|-------|-------|-----------------|
| 1. Internal refactor | Module decomposition, Write-Host extraction | Large |
| 2. New CLI + legacy shim | dlss-toolkit.ps1, flag mapping, routing | Large |
| 3. Safety + logging | Backup, manifest, elevation, error messages | Large |
| 4. Doctor + restore | New commands, first-run UX | Medium |
| 5. Packaging + CI | Installers, .cmd, GitHub Actions, manifests | Medium |
| 6. Documentation | README, help, safety promises | Medium |

## Risk Mitigations

| Risk | Mitigation |
|------|-----------|
| PS5.1 `??`/`?:` syntax slip | PSScriptAnalyzer rule PSUseCompatibleSyntax |
| Write-Host extraction breaks upgrade flow | Decompose Start-DLSSUpgrade into composable steps first |
| Public API change breaks existing users | Keep old function names as aliases |
| Backup location change makes old backups invisible | Restore scans both old and new locations |
| `-All` behavior change surprises users | Deprecation warning + requires `-Yes` for auto-apply |
| `$stagingFolder` unassigned bug | Fix in Phase 1 before refactoring upgrade flow |
| GUI scope explosion | Defer to v2.0 unconditionally |
