# Research: DLSS Version Toolkit

**Branch**: `001-dlss-version-checker` | **Date**: 2026-04-20 | **Phase**: 0

---

## 1. PowerShell Module Structure Best Practices

### Decision

Use the standard PowerShell module pattern: `DLSSVersion.psm1` (implementation) + `DLSSVersion.psd1` (manifest). The `.psm1` file contains all function definitions and uses `Export-ModuleMember` to explicitly control the public surface. The `.psd1` manifest declares module metadata (GUID, version, author, description, exported functions, minimum PowerShell version). Place both files in `src/` to separate module code from the standalone script entry point.

### Rationale

- PowerShell Gallery requires a `.psd1` manifest for `Publish-Module`. Without it, the module cannot be distributed via the standard channel.
- `Export-ModuleMember -Function Get-DLSSVersions, Get-DLSSLatestVersion, Start-DLSSUpgrade` prevents internal helper functions from leaking into the user's session.
- The manifest's `FunctionsToExport` field enables tab completion and `Get-Command -Module DLSSVersion` without loading the entire module.
- Placing module files in `src/` keeps the repository root clean (only `check-dlss-versions.ps1` and `README.md` at root) while allowing `Import-Module ./src/DLSSVersion.psd1` for development.

### Alternatives Considered

- **Single .ps1 file (no module)**: Simpler structure but blocks PowerShell Gallery distribution, no proper cmdlet naming, no Get-Help integration, no version tracking. Rejected per Constitution Principle IV.
- **Flat root layout (psm1/psd1 at root)**: Would work but clutters root with module files alongside the standalone script. The `src/` directory provides a clean separation between "entry point" and "implementation."
- **Nested module with submodules**: Overkill for ~200-300 LOC. Adds unnecessary directory depth and complexity. Rejected per Constitution Principle V (Simplicity Over Flexibility).

---

## 2. PowerShell 5.1 Compatibility Handling

### Decision

Enforce PS5.1 syntax throughout all code. Use `if/else` instead of ternary `? :`, explicit null checks with `if ($null -ne $var)` instead of `??`, and separate statements instead of `&&`/`||` chain operators. Use `$PSVersionTable.PSVersion` for runtime version detection if needed. Avoid `foreach -Parallel`, `ConstrainedLanguage` mode assumptions, and any cmdlet introduced after PS5.1.

### Rationale

- Windows 10 ships with PowerShell 5.1 by default. Many users will not have PS7 installed, and requiring it violates Constitution Principle I (Zero Dependencies).
- PS7-only syntax causes parse errors in PS5.1, not just runtime errors. The script will fail to load entirely, not just behave differently.
- The existing `check-dlss-versions.ps1` already uses PS5.1-compatible patterns (if/else, -match, try/catch). The module must maintain this compatibility.
- Specific patterns to avoid and their replacements:
  - `$x ? $x : $default` -> `if ($x) { $x } else { $default }`
  - `$x ?? $default` -> `if ($null -ne $x) { $x } else { $default }`
  - `cmd1 && cmd2` -> `cmd1; if ($LASTEXITCODE -eq 0) { cmd2 }` or `cmd1; if ($?) { cmd2 }`
  - `cmd1 || cmd2` -> `cmd1; if (-not $?) { cmd2 }`

### Alternatives Considered

- **Require PS7**: Would enable modern syntax and better performance. Rejected: violates Constitution Principle I and III (Windows-First explicitly states "no PS7+ features").
- **Dual codebase with #requires**: Would double maintenance surface for a ~200 LOC tool. Rejected per Constitution Principle V.
- **Transpilation via PSCompat**: Adds a build step and external dependency. Rejected per Constitution Principle I.

---

## 3. Backup Strategy for Safe Upgrades

### Decision

Before any upgrade operation, create a timestamped backup of the entire Release version folder (not just individual DLLs) to a subdirectory within the same parent path: `C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\.dlss-backup-<yyyyMMdd-HHmmss>`. Copy all files (DLLs + config) from the target Release build folder into the backup. After backup verification (confirm backup directory exists and file count matches source), proceed with the upgrade. If any copy operation fails mid-upgrade, restore all files from the backup to their original locations. On successful upgrade completion, leave the backup in place (do not auto-delete) so users can manually roll back if needed.

### Rationale

- Constitution Principle II (Safe by Default) mandates backup before writes and restore on failure.
- Backing up the entire folder (rather than individual files) is simpler and guarantees a complete restore point. Individual file backups require tracking which files were modified vs. created, which adds complexity.
- Using a `.dlss-backup-` prefix with timestamp keeps backups discoverable and ordered. The dot prefix makes them sort separately from version build folders.
- Leaving backups in place (no auto-delete) is the safest default. Users can manually clean up or re-run the tool to check backup status. Auto-deletion risks removing the only recovery path if the user discovers a problem later.
- The backup location is within the same ProgramData path, avoiding permission issues (the user already has write access if they triggered an upgrade).

### Alternatives Considered

- **Individual file backup (per-DLL)**: More granular but requires tracking state across multiple files. If the config file copy fails after two DLLs succeed, partial restore is complex. Folder-level backup is atomic by comparison.
- **Backup to temp directory ($env:TEMP)**: Simpler path but risks cleanup by other processes or system reboots. Also introduces a different ACL context that might cause permission issues.
- **Shadow copy (VSS)**: Overkill for a few small files. Requires admin elevation for VSS API calls. Rejected per Constitution Principle V.
- **No backup (trust the operation)**: Directly violates Constitution Principle II. Rejected.

---

## 4. Encoding Handling on Windows

### Decision

Use `-Raw` flag for all `Get-Content` calls on configuration files. This reads the entire file as a single string, preserving line endings and avoiding array-of-lines overhead. For `Set-Content`, use `-Encoding UTF8` explicitly. Never rely on the default encoding (which varies between PS5.1 and PS7). When parsing `nvngx_package_config.txt`, use `-Raw` to get the full content as a string before regex matching, which avoids line-by-line encoding issues. For upgrade file copies, prefer `Copy-Item` over read-then-write patterns to preserve the original file encoding exactly.

### Rationale

- PS5.1 defaults to UTF-16LE for `Set-Content` and the system locale encoding for `Get-Content`. PS7 defaults to UTF-8 without BOM. This mismatch causes corruption if the tool is run under different PowerShell versions.
- The existing script already uses `-Raw` for `Get-Content` (lines 21, 40, 111 of `check-dlss-versions.ps1`). This pattern is proven and must be preserved.
- NVIDIA's `nvngx_package_config.txt` files are UTF-8 encoded. Using `-Raw` reads them correctly regardless of the console's current code page.
- For upgrade operations, `Copy-Item` preserves the source file's encoding byte-for-byte. This is safer than `Get-Content | Set-Content` which re-encodes through PowerShell's encoding pipeline.
- For console output (Write-Host), cp1252 limitations are acceptable since we only display ASCII-safe version numbers and labels. No special handling needed for output.
- Constitution Principle III (Windows-First) explicitly requires `-Encoding UTF8` or `-Raw` for file I/O.

### Alternatives Considered

- **Explicit BOM handling**: Read with `-Encoding UTF8` and write with `-Encoding UTF8`. Risk: PS5.1's `-Encoding UTF8` writes UTF-8 with BOM, which might confuse NVIDIA's parser. Using `-Raw` for reads avoids this; for writes, we copy the staging config file directly with `Copy-Item` rather than reading and re-writing content.
- **[System.IO.File]::ReadAllText()**: More explicit encoding control but adds .NET API surface for a simple file read. `Get-Content -Raw` is idiomatic PowerShell and sufficient.
- **Encoding-agnostic approach (try both)**: Overly complex. The NVIDIA config files are consistently UTF-8. Rejected per Constitution Principle V.

---

## 5. Distribution Packaging

### Decision

Primary: PowerShell Gallery via `Publish-Module`. Secondary: Scoop manifest in a separate `scoop/` directory or a dedicated bucket repository. Tertiary: winget YAML manifest submitted as a PR to the `microsoft/winget-pkgs` repository.

**PowerShell Gallery**: The `.psd1` manifest already contains all required metadata (GUID, version, author, description, FunctionsToExport). Publishing is `Publish-Module -Path ./src -NuGetApiKey <key>`. Users install with `Install-Module DLSSVersion -Scope CurrentUser`.

**Scoop**: Create a `dlss-version-toolkit.json` manifest with URL to the PowerShell Gallery module or a GitHub release ZIP. The manifest specifies the PowerShell script as the install target. Users install with `scoop install dlss-version-toolkit`.

**winget**: Create a `DLSSVersionToolkit.yaml` manifest following the winget manifest schema (version 1.6+). Submit as a PR to `microsoft/winget-pkgs`. The installer type is `zip` or `portable` since this is a PowerShell module, not an MSI. Users install with `winget install DLSSVersionToolkit`.

### Rationale

- PowerShell Gallery is the native distribution channel for PowerShell modules. It provides `Install-Module`, `Update-Module`, and `Find-Module` out of the box. This is the primary channel per Constitution Principle IV.
- Scoop is popular among Windows power users and developers. A Scoop manifest is a single JSON file with minimal maintenance overhead.
- winget is the built-in Windows package manager (ships with Windows 11, available on Windows 10). It has the broadest reach but the highest submission friction (manual PR to microsoft/winget-pkgs).
- The standalone script (`check-dlss-versions.ps1`) enables zero-install usage: download and run. This covers users who do not use any package manager.

### Alternatives Considered

- **Chocolatey**: Requires a `.nuspec` file and Chocolatey-specific packaging conventions. Adds a third packaging format for marginal additional reach. winget + Scoop + PowerShell Gallery covers the Windows audience sufficiently. Rejected per Constitution Principle V.
- **GitHub Releases only**: No package manager integration. Users must download ZIPs manually. Does not meet spec requirement FR-010 (packageable for standard Windows distribution channels).
- **Self-extracting EXE**: Requires a build tool (like ps2exe or Invoke-Build). Adds a compiled artifact that cannot be inspected. Violates Constitution Principle I (Zero Dependencies) for the build step and Principle V (Simplicity).

---

## 6. Pester Test Patterns for Filesystem-Dependent Code

### Decision

Use Pester 5.x with `$TestDrive` fixtures for filesystem-dependent tests. Create a temporary NGX directory structure in `BeforeAll` blocks using Pester's built-in `$TestDrive` variable (auto-cleaned per Describe block). Build sample `nvngx_package_config.txt` files and dummy `nvngx_*.dll` files in the fixture. Inject the fixture path into module functions via a `-Path` parameter (with default to the real NGX path), enabling both production use and test isolation.

### Rationale

- Pester's `$TestDrive` provides an isolated temporary directory that is automatically cleaned up after each `Describe` block. This avoids test pollution and does not require manual cleanup.
- The real NVIDIA NGX directories require NVIDIA software to be installed and may require admin permissions. Tests must run on any development machine without these prerequisites.
- Adding a `-Path` parameter to `Get-DLSSVersions` and `Start-DLSSUpgrade` is a minimal change that enables testability without adding complexity. The default value points to the real path, so production usage is unchanged.
- This approach follows the "dependency injection via parameter default" pattern, which is idiomatic in PowerShell testing.
- Test fixture structure:
  ```
  $TestDrive/
  └── NGX/
      └── models/
          └── dlss_override/
              └── versions/
                  ├── Build_001/
                  │   ├── subfolder/
                  │   │   ├── nvngx_dlss.dll
                  │   │   ├── nvngx_dlssg.dll
                  │   │   └── nvngx_package_config.txt
                  └── Build_002/
                      └── subfolder/
                          ├── nvngx_dlss.dll
                          ├── nvngx_dlssg.dll
                          └── nvngx_package_config.txt
  ```

### Alternatives Considered

- **Mock Get-ChildItem and Get-Content**: Pester's `Mock` can intercept cmdlet calls. However, mocking core cmdlets makes tests brittle and does not test the actual filesystem interaction logic (path construction, file discovery). Real fixture files test the actual code path. Mocks are acceptable for error scenarios (permission denied, corrupted content) where creating real error conditions is impractical.
- **Test against real NGX directories**: Requires NVIDIA software installed on the CI/CD machine. Not portable. Tests would fail on any machine without the exact directory structure.
- **Environment variable for path override**: More global than a parameter. Risks leaking state between tests if not carefully reset. The `-Path` parameter is scoped per call and cleaner.
- **Registry-based fixture**: Overkill. The tool reads files, not registry keys. File-based fixtures are simpler and more representative.