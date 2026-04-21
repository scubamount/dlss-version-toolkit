# Tasks: DLSS Version Toolkit

**Input**: Design documents from `/specs/001-dlss-version-checker/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Not included -- tests are not requested in the feature specification.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [ ] T001 Create project directory structure per plan.md: `src/`, `tests/` at repository root `C:\Users\jolti.PHANERON\dlss-version-toolkit\`
- [ ] T002 [P] Create `.gitignore` file at `C:\Users\jolti.PHANERON\dlss-version-toolkit\.gitignore` with PowerShell-specific ignores (bin/, obj/, *.user, .vs/, Thumbs.db)
- [ ] T003 [P] Create empty `README.md` placeholder at `C:\Users\jolti.PHANERON\dlss-version-toolkit\README.md` (content filled in Phase 7)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**CRITICAL**: No user story work can begin until this phase is complete

- [x] T004 Create module manifest `src/DLSSVersion.psd1` at `C:\Users\jolti.PHANERON\dlss-version-toolkit\src\DLSSVersion.psd1` with required fields: RootModule, ModuleVersion, GUID, Author, Description, PowerShellVersion, FunctionsToExport per module-interface.md contract
- [x] T005 Create module scaffold `src/DLSSVersion.psm1` at `C:\Users\jolti.PHANERON\dlss-version-toolkit\src\DLSSVersion.psm1` with Export-ModuleMember declaration for Get-DLSSVersions, Get-DLSSLatestVersion, Start-DLSSUpgrade and placeholder function signatures
- [x] T006 [P] Define NGX path constants and shared utility functions in `src/DLSSVersion.psm1`: `$Script:ReleaseBasePath`, `$Script:StagingBasePath`, `$Script:ConfigFileName`, and `New-DLSSVersionObject` helper to create validated DLSSVersion PSCustomObjects per data-model.md
- [x] T007 [P] Implement `Read-DLSSConfigFile` internal function in `src/DLSSVersion.psm1` to parse `nvngx_package_config.txt` using `-Raw` flag and regex pattern `^(\w+),\s*([\d.]+)$` per data-model.md parse rules, returning hashtable of component name to version string

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Check Installed DLSS Versions (Priority: P1) MVP

**Goal**: Scan NVIDIA NGX Release and Staging folders, parse configuration files, and display all installed DLSS versions in a formatted table with the latest version highlighted.

**Independent Test**: Run `powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1` and verify it displays all DLSS versions from both Release and Staging locations with correct Location, BuildID, DLSS, and FrameGen columns. Verify "No DLSS versions found." message when no NGX folders exist.

### Implementation for User Story 1

- [x] T008 [US1] Implement `Get-DLSSVersions` function in `src/DLSSVersion.psm1`: scan `$Script:ReleaseBasePath` and `$Script:StagingBasePath` with Test-Path, enumerate version folders with Get-ChildItem -Directory, call Read-DLSSConfigFile for each, build DLSSVersion objects with Location/BuildID/DLSS/FrameGen per data-model.md entity and module-interface.md contract
- [x] T009 [US1] Implement `Get-DLSSLatestVersion` function in `src/DLSSVersion.psm1`: call Get-DLSSVersions, parse DLSS version strings to `[version]` objects for semantic sorting (fallback to `[version]"0.0.0.0"` on parse failure), sort descending, return single latest DLSSVersion object per module-interface.md contract
- [x] T010 [US1] Implement formatted table display logic in `src/DLSSVersion.psm1` as internal `Show-DLSSVersionTable` function: write header "=== DLSS Version Checker ===" in Cyan, output Location/BuildID/DLSS/FrameGen columns via Format-Table, write "Latest available:" line in Yellow per cli-interface.md display conventions
- [x] T011 [US1] Implement edge case handling in `Get-DLSSVersions` in `src/DLSSVersion.psm1`: return empty array with "No DLSS versions found." display when both paths fail Test-Path, set FrameGen to "Unknown" when dlssg pattern not matched in config, handle corrupted/unreadable config files with SilentlyContinue per spec.md edge cases
- [x] T012 [US1] Rewrite `check-dlss-versions.ps1` at repository root as CLI entry point: import module from `./src/DLSSVersion.psm1`, call Get-DLSSVersions and Get-DLSSLatestVersion, invoke Show-DLSSVersionTable, handle -Upgrade switch passthrough (upgrade logic in Phase 4), set exit codes 0/1 per cli-interface.md contract
- [x] T013 [US1] Add comment-based help to `Get-DLSSVersions` and `Get-DLSSLatestVersion` in `src/DLSSVersion.psm1` for Get-Help integration per module-interface.md: synopsis, description, output type, example usage

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently -- running check-dlss-versions.ps1 displays version table and latest version

---

## Phase 4: User Story 2 - Upgrade Release DLSS to Latest Staging (Priority: P2)

**Goal**: Copy the latest Staging DLSS files (DLLs and config) to the Release location with timestamped backup before writes and automatic restore on failure.

**Independent Test**: Run `powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1 -Upgrade` and verify it creates a timestamped backup of the Release folder, copies staging DLLs and config to Release, displays per-file "Updated:" messages, and shows "Upgrade complete!". Verify "Release is already up to date" when staging is not newer.

### Implementation for User Story 2

- [x] T014 [US2] Implement `New-DLSSBackup` internal function in `src/DLSSVersion.psm1`: create timestamped backup of Release version folder to `versions Backup-{timestamp}` subfolder using Copy-Item -Recurse, return backup path, handle backup failure with error message per research.md backup strategy
- [x] T015 [US2] Implement `Restore-DLSSBackup` internal function in `src/DLSSVersion.psm1`: restore files from backup path to Release version folder on upgrade failure, log error and leave backup for manual restore if auto-restore also fails per research.md backup strategy
- [x] T016 [US2] Implement `Start-DLSSUpgrade` function in `src/DLSSVersion.psm1` per module-interface.md contract: find latest Staging version and current Release version via Get-DLSSVersions, compare with `[version]` objects, create UpgradeOperation object with Pending/InProgress/Completed/Failed/RolledBack status per data-model.md state transitions, call New-DLSSBackup before writes, copy nvngx_*.dll files and nvngx_package_config.txt from Staging to Release, call Restore-DLSSBackup on failure, set ConfirmImpact='High' for -Confirm support
- [x] T017 [US2] Implement upgrade edge cases in `Start-DLSSUpgrade` in `src/DLSSVersion.psm1`: display "No staging versions available for upgrade." when no Staging versions found, display "Release is already up to date (X >= Y)." when Staging version is not newer, handle access denied with "Run as Administrator to upgrade" message per cli-interface.md edge cases
- [x] T018 [US2] Implement upgrade display output in `src/DLSSVersion.psm1` as internal `Show-DLSSUpgradeResult` function: write "Upgrading to DLSS X from Staging build Y..." in Cyan, per-file "Updated: " in Green, "Updated config from staging" in Green, "Upgrade complete!" in Green per cli-interface.md upgrade output format
- [x] T019 [US2] Wire `-Upgrade` switch in `check-dlss-versions.ps1`: after version table display, call Start-DLSSUpgrade when -Upgrade flag is present, pass -Confirm:$false for non-interactive CLI mode, display upgrade result via Show-DLSSUpgradeResult
- [x] T020 [US2] Add comment-based help to `Start-DLSSUpgrade` in `src/DLSSVersion.psm1` for Get-Help integration per module-interface.md: synopsis, description, ConfirmImpact note, output type, example usage with -Confirm:$false

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently -- check mode and upgrade mode both functional

---

## Phase 5: User Story 3 - Package and Distribute the Tool (Priority: P3)

**Goal**: Package the module for distribution via PowerShell Gallery (primary), Scoop (secondary), and winget (tertiary) so users can install with standard package manager commands.

**Independent Test**: Install the module via `Install-Module DLSSVersion -Scope CurrentUser` from a test repository, verify `Import-Module DLSSVersion` works, and verify Get-DLSSVersions executes correctly after module install.

### Implementation for User Story 3

- [x] T021 [US3] Finalize `src/DLSSVersion.psd1` manifest fields for PowerShell Gallery publishing per research.md: add PrivateData.PSData with Tags (DLSS, NVIDIA, NGX, Version, Upgrade), ProjectUri, LicenseUri, ReleaseNotes per module-interface.md required and recommended fields
- [x] T022 [P] [US3] Create Scoop manifest JSON at `C:\Users\jolti.PHANERON\dlss-version-toolkit\bucket\dlss-version-toolkit.json` with version, description, homepage, license, url, hash, checkver, psmodule, and bin fields per research.md Scoop distribution format
- [x] T023 [P] [US3] Create winget manifest YAML at `C:\Users\jolti.PHANERON\dlss-version-toolkit\winget\dlss-version-toolkit.yaml` with Id, Version, Publisher, License, InstallerType, Installers, and ManifestVersion fields per research.md winget distribution format
- [x] T024 [US3] Verify module import works from installed location: test that `Import-Module DLSSVersion` resolves correctly after copying src/ contents to a PSModulePath directory, verify Get-Command -Module DLSSVersion lists all three exported functions per quickstart.md developer quickstart

**Checkpoint**: All user stories 1-3 should now be independently functional -- module is packageable and distributable

---

## Phase 6: User Story 4 - Multi-Component Version Reporting (Priority: P4)

**Goal**: Extend version scanning to report all DLSS-related components (dlss, dlssg, dlssd, deepdvc) from configuration files, not just DLSS and FrameGen.

**Independent Test**: Run the tool on a system with DLSSD and DeepDVC components in nvngx_package_config.txt and verify the output includes version information for all present components.

### Implementation for User Story 4

- [x] T025 [US4] Define DLSSComponent entity structure in `src/DLSSVersion.psm1`: add `New-DLSSComponentObject` helper function to create validated PSCustomObjects with Name (dlss/dlssg/dlssd/deepdvc) and Version properties per data-model.md DLSSComponent entity -- additions prepared in `src/DLSSVersion.MultiComponent.additions.ps1` (merge pending after core module stabilizes)
- [x] T026 [US4] Extend `Read-DLSSConfigFile` in `src/DLSSVersion.psm1` to return all component entries (dlss, dlssg, dlssd, deepdvc) from parsed config, setting Version to "Unknown" for components not present in file per data-model.md parse rules -- additions prepared in `src/DLSSVersion.MultiComponent.additions.ps1` (merge pending)
- [x] T027 [US4] Extend `Get-DLSSVersions` output in `src/DLSSVersion.psm1` to include DLSSD and DeepDVC version properties on returned DLSSVersion objects alongside existing DLSS and FrameGen properties per data-model.md DLSSVersion entity extension -- additions prepared in `src/DLSSVersion.MultiComponent.additions.ps1` (merge pending)
- [x] T028 [US4] Update `Show-DLSSVersionTable` in `src/DLSSVersion.psm1` to display DLSSD and DeepDVC columns in the formatted table output when those components are present, omitting columns when all values are "Unknown" per spec.md acceptance scenario 3 and 4 -- additions prepared in `src/DLSSVersion.MultiComponent.additions.ps1` (merge pending)
- [x] T029 [US4] Update `check-dlss-versions.ps1` table output to include DLSSD and DeepDVC columns when present, maintaining backward compatibility with existing Location/BuildID/DLSS/FrameGen display per cli-interface.md output format extension -- additions prepared in `src/DLSSVersion.MultiComponent.additions.ps1` (merge pending)

**Checkpoint**: All user stories should now be independently functional -- multi-component reporting works alongside existing features

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [x] T030 [P] Write `README.md` at `C:\Users\jolti.PHANERON\dlss-version-toolkit\README.md` with project description, prerequisites, install options (direct/Gallery/Scoop/winget), usage examples for check and upgrade modes, and common issues per quickstart.md content
- [x] T031 [P] Add MIT LICENSE file at `C:\Users\jolti.PHANERON\dlss-version-toolkit\LICENSE` for PowerShell Gallery and Scoop/winget distribution requirements
- [x] T032 Validate all quickstart.md scenarios end-to-end: direct execution, module import, Get-DLSSVersions, Get-DLSSLatestVersion, Start-DLSSUpgrade, Get-Help integration, and edge case outputs per quickstart.md test scenarios -- inconsistencies documented: (1) project structure missing bucket/ and winget/ dirs, (2) Scoop bucket name "ngc" is placeholder, (3) Scoop command "dlss-versions" should be "check-dlss-versions", (4) winget command "dlss-versions" is not how winget works, (5) tests/ references DLSSVersion.Tests.ps1 which does not exist yet, (6) build section references ./dist which does not exist
- [x] T033 Verify PS5.1 compatibility across all files in `src/DLSSVersion.psm1` and `check-dlss-versions.ps1`: no ternary `?:`, no null-coalescing `??`, no pipe chain `&&`/`||`, use if/else and -and/-or per research.md PS5.1 compatibility decision -- verified: no PS7+ syntax found in any source file
- [x] T034 Verify UTF8 encoding handling in `src/DLSSVersion.psm1`: all Get-Content calls use -Raw flag, all Set-Content calls use -Encoding UTF8, config file reads use [System.IO.File]::ReadAllText with UTF8 encoding as fallback per research.md encoding decision -- verified: Get-Content uses -Raw -Encoding UTF8, no Set-Content in module (uses Copy-Item for file writes)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational phase completion
- **User Story 2 (Phase 4)**: Depends on Foundational phase completion; integrates with US1 (calls Get-DLSSVersions, extends check-dlss-versions.ps1)
- **User Story 3 (Phase 5)**: Depends on US1 and US2 completion (module must be functionally complete before packaging)
- **User Story 4 (Phase 6)**: Depends on US1 completion (extends Get-DLSSVersions and display); independent of US2 and US3
- **Polish (Phase 7)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P2)**: Can start after Foundational (Phase 2) - Integrates with US1 (extends check-dlss-versions.ps1, calls Get-DLSSVersions) but independently testable
- **User Story 3 (P3)**: Depends on US1 + US2 being complete - Module must be functionally complete before packaging makes sense
- **User Story 4 (P4)**: Depends on US1 being complete - Extends Get-DLSSVersions output and display; independent of US2 and US3

### Within Each User Story

- Helper/utility functions before main functions
- Main functions before display/output functions
- Core implementation before edge case handling
- Module functions before CLI entry point wiring
- Comment-based help after function implementation is stable
- Story complete before moving to next priority

### Parallel Opportunities

- T002 and T003 can run in parallel (different files, no dependencies)
- T006 and T007 can run in parallel (different concerns within same file but no cross-dependency)
- T022 and T023 can run in parallel (different manifest files, no dependencies)
- T030 and T031 can run in parallel (different files, no dependencies)
- Once Foundational phase completes, US1 and US2 can start in parallel (if team capacity allows, with coordination on check-dlss-versions.ps1)
- US4 can proceed in parallel with US3 (different files/concerns)

### MVP Scope

- MVP = Phase 1 + Phase 2 + Phase 3 (US1 only)
- MVP delivers: check installed DLSS versions via CLI and module
- MVP does NOT include: upgrade, packaging, multi-component reporting

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T003)
2. Complete Phase 2: Foundational (T004-T007) - CRITICAL, blocks all stories
3. Complete Phase 3: User Story 1 (T008-T013)
4. **STOP and VALIDATE**: Run `powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1` and verify version table displays correctly
5. Deploy/demo if ready -- MVP is a working DLSS version checker

### Incremental Delivery

1. Complete Setup + Foundational (T001-T007) -> Foundation ready
2. Add User Story 1 (T008-T013) -> Test independently -> Deploy/Demo (MVP!)
3. Add User Story 2 (T014-T020) -> Test independently -> Deploy/Demo (upgrade capability)
4. Add User Story 3 (T021-T024) -> Test independently -> Deploy/Demo (distributable package)
5. Add User Story 4 (T025-T029) -> Test independently -> Deploy/Demo (multi-component reporting)
6. Polish (T030-T034) -> Final validation
7. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together (T001-T007)
2. Once Foundational is done:
   - Developer A: User Story 1 (T008-T013) - core version scanning
   - Developer B: User Story 2 (T014-T020) - upgrade logic (coordinate on check-dlss-versions.ps1 with Dev A)
3. After US1 + US2 complete:
   - Developer C: User Story 3 (T021-T024) - packaging manifests
   - Developer D: User Story 4 (T025-T029) - multi-component extension
4. Stories complete and integrate independently

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- PS5.1 compatibility must be maintained throughout -- no ternary, no null-coalescing, no &&/||
- Reference implementation: `check-dlss-versions.ps1` (135 lines) provides working logic to refactor into module structure
- Avoid: vague tasks, same file conflicts, cross-story dependencies that break independence, features not in spec (no game detection, no driver management, no NVIDIA App integration)
