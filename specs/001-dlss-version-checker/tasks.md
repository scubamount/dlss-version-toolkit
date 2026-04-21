---

description: "Task list for DLSS Version Toolkit feature implementation"
---

# Tasks: DLSS Version Toolkit

**Input**: Design documents from `/specs/001-dlss-version-checker/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/cli-interface.md, contracts/module-interface.md

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [ ] T001 Create project directory structure per plan.md: dlss-version-toolkit/src/, dlss-version-toolkit/tests/
- [ ] T002 Create .gitignore file in dlss-version-toolkit/.gitignore with PowerShell, Windows, and module patterns

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**CRITICAL**: No user story work can begin until this phase is complete

- [ ] T003 [P] Implement shared utility functions in dlss-version-toolkit/src/DLSSVersion.psm1: NGX path constants, version parsing
- [ ] T004 [P] Implement DLSSVersion entity constructor in dlss-version-toolkit/src/DLSSVersion.psm1: Location, BuildID, DLSS, FrameGen properties
- [ ] T005 Create module manifest dlss-version-toolkit/src/DLSSVersion.psd1 with RootModule, ModuleVersion, GUID, FunctionsToExport

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Check Installed DLSS Versions (Priority: P1) MVP

**Goal**: Scan and display installed DLSS versions from Release and Staging locations with version numbers and Build IDs

**Independent Test**: Run check-dlss-versions.ps1 without -Upgrade flag and verify it displays all DLSS versions from both locations with correct version numbers

### Implementation for User Story 1

- [ ] T006 [P] [US1] Implement Get-DLSSVersions function in dlss-version-toolkit/src/DLSSVersion.psm1: scan Release path C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions
- [ ] T007 [P] [US1] Implement Get-DLSSVersions function in dlss-version-toolkit/src/DLSSVersion.psm1: scan Staging path C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions
- [ ] T008 [US1] Implement config file parsing for dlss and dlssg versions in dlss-version-toolkit/src/DLSSVersion.psm1: nvngx_package_config.txt extraction
- [ ] T009 [US1] Implement Get-DLSSLatestVersion function in dlss-version-toolkit/src/DLSSVersion.psm1: semantic version comparison
- [ ] T010 [US1] Implement table output formatting in dlss-version-toolkit/check-dlss-versions.ps1: header, Location/BuildID/DLSS/FrameGen columns
- [ ] T011 [US1] Implement CLI entry point in dlss-version-toolkit/check-dlss-versions.ps1: default check mode execution
- [ ] T012 [US1] Add edge case handling: no DLSS installed, access denied errors

**Checkpoint**: At this point, User Story 1 should be fully functional - running check-dlss-versions.ps1 displays all installed DLSS versions

---

## Phase 4: User Story 2 - Upgrade Release DLSS to Latest Staging (Priority: P2)

**Goal**: Copy latest Staging DLSS files to Release location when Staging version is newer

**Independent Test**: Run check-dlss-versions.ps1 with -Upgrade flag and verify it copies files, then running without -Upgrade shows upgraded version

### Implementation for User Story 2

- [ ] T013 [P] [US2] Implement UpgradeOperation entity in dlss-version-toolkit/src/DLSSVersion.psm1: SourceVersion, TargetVersion, Status, BackupPath, ErrorMessage
- [ ] T014 [P] [US2] Implement backup creation logic in dlss-version-toolkit/src/DLSSVersion.psm1: timestamped folder in C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions
- [ ] T015 [US2] Implement Start-DLSSUpgrade function in dlss-version-toolkit/src/DLSSVersion.psm1: version comparison, upgrade eligibility check
- [ ] T016 [US2] Implement file copy logic in dlss-version-toolkit/src/DLSSVersion.psm1: copy DLL files from Staging to Release
- [ ] T017 [US2] Implement config copy in dlss-version-toolkit/src/DLSSVersion.psm1: nvngx_package_config.txt from Staging to Release
- [ ] T018 [US2] Add -Upgrade flag to dlss-version-toolkit/check-dlss-versions.ps1: CLI integration with backup creation
- [ ] T019 [US2] Add edge case handling: Upgrade when no staging, Release already up to date, backup failures

**Checkpoint**: At this point, User Story 2 should be functional - dlss-version-toolkit/check-dlss-versions.ps1 -Upgrade copies files

---

## Phase 5: User Story 3 - Package and Distribute the Tool (Priority: P3)

**Goal**: Package for distribution via PowerShell Gallery, Scoop, and winget

**Independent Test**: Install from distribution channel and verify commands work as expected

### Implementation for User Story 3

- [ ] T020 [P] [US3] Update dlss-version-toolkit/src/DLSSVersion.psd1 with PowerShell Gallery metadata: Author, Description, Tags, ProjectUri, LicenseUri
- [ ] T021 [P] [US3] Create Scoop manifest in dlss-version-toolkit/bucket/dlss-version-toolkit.json: version, description, url, hash
- [ ] T022 [US3] Create winget YAML in dlss-version-toolkit/winget/dlss-version-toolkit.yaml: Id, Version, Publisher, Installer
- [ ] T023 [US3] Implement Get-Help integration in dlss-version-toolkit/src/DLSSVersion.psm1: comment-based help for exported functions

**Checkpoint**: At this point, User Story 3 should be functional - module can be published to and installed from distribution channels

---

## Phase 6: User Story 4 - Multi-Component Version Reporting (Priority: P4)

**Goal**: Display versions for all DLSS-related components: DLSS, Frame Gen, DLSSD, DeepDVC

**Independent Test**: Run check-dlss-versions.ps1 and verify output includes all available components with their versions

### Implementation for User Story 4

- [ ] T024 [P] [US4] Implement DLSSComponent entity in dlss-version-toolkit/src/DLSSVersion.psm1: Name, Version properties
- [ ] T025 [US4] Implement multi-component parsing in dlss-version-toolkit/src/DLSSVersion.psm1: dlssd and deepdvc version extraction
- [ ] T026 [US4] Update table output in dlss-version-toolkit/check-dlss-versions.ps1: include additional component columns
- [ ] T027 [US4] Add edge case handling: component not present, Unknown version for missing components

**Checkpoint**: At this point, User Story 4 should be functional - tool shows DLSS, FrameGen, DLSSD, DeepDVC versions

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [ ] T028 [P] Create dlss-version-toolkit/README.md: installation, usage, troubleshooting
- [ ] T029 Update quickstart.md validation: test scenarios from quickstart.md
- [ ] T030 Remove unused functions and code cleanup in dlss-version-toolkit/src/DLSSVersion.psm1

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion
  - User stories can then proceed in parallel (if staffed)
  - Or sequentially in priority order (P1 -> P2 -> P3 -> P4)
- **Polish (Final Phase)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P2)**: Requires US1 complete (uses Get-DLSSVersions for upgrade eligibility check)
- **User Story 3 (P3)**: Can start after Foundational (Phase 2) - Independent of other stories
- **User Story 4 (P4)**: Requires US1 complete (extends version scanning logic)

### Within Each User Story

- Entities before functions
- Functions before integration
- Core implementation before CLI integration
- Story complete before moving to next priority

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel
- All Foundational tasks marked [P] can run in parallel (within Phase 2)
- Once Foundational phase completes, all user stories can start in parallel (if team capacity allows)
- Entities within a story marked [P] can run in parallel
- Different user stories can be worked on in parallel by different team members

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Test User Story 1 independently
5. Deploy/demo if ready

### Incremental Delivery

1. Complete Setup + Foundational -> Foundation ready
2. Add User Story 1 -> Test independently -> Deploy/Demo (MVP!)
3. Add User Story 2 -> Test independently -> Deploy/Demo
4. Add User Story 3 -> Test independently -> Deploy/Demo
5. Add User Story 4 -> Test independently -> Deploy/Demo
6. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1
   - Developer B: User Story 2
   - Developer C: User Story 3
3. Stories complete and integrate independently

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Avoid: vague tasks, same file conflicts, cross-story dependencies that break independence
- Tests NOT included per spec (tests not requested in design documents)