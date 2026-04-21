---

description: "Task list template for feature implementation"
---

# Tasks: DLSS Version Toolkit

**Input**: Design documents from `/specs/001-dlss-version-checker/`
**Prerequisites**: plan.md (required), spec.md (required for user stories)

**Tests**: Pester tests are included to ensure reliability for a tool that modifies system files.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [ ] T001 [P] Create project structure per implementation plan in C:\Users\jolti.PHANERON\dlss-version-toolkit\src\
- [ ] T002 [P] Create tests directory at C:\Users\jolti.PHANERON\dlss-version-toolkit\tests\
- [ ] T003 Initialize Pester testing framework (install Pester module or add to RequiredModules)

---

## Phase 2: Foundational (Module Structure)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T004 [P] Create DLSSVersion.psd1 module manifest at C:\Users\jolti.PHANERON\dlss-version-toolkit\src\DLSSVersion.psd1
- [ ] T005 [P] Create DLSSVersion.psm1 module file at C:\Users\jolti.PHANERON\dlss-version-toolkit\src\DLSSVersion.psm1
- [ ] T006 Define module constants (paths, version patterns) in DLSSVersion.psm1
- [ ] T007 Implement Test-NGXFolderExists helper function in DLSSVersion.psm1

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Check Installed DLSS Versions (Priority: P1) 🎯 MVP

**Goal**: Enable users to scan and display all installed DLSS versions from Release and Staging locations

**Independent Test**: Run Get-DLSSVersions and verify it returns all installed versions with correct Build IDs, DLSS versions, and FrameGen versions

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T008 [P] [US1] Write Pester test for Get-DLSSVersions function in tests/DLSSVersion.Tests.ps1
- [ ] T009 [P] [US1] Write Pester test for version parsing from nvngx_package_config.txt

### Implementation for User Story 1

- [ ] T010 [P] [US1] Implement Get-DLSSVersions function in src/DLSSVersion.psm1 (scans Release and Staging folders)
- [ ] T011 [P] [US1] Implement Get-ComponentVersion function to extract version from config file using regex pattern
- [ ] T012 [US1] Implement Get-DLSSLatestVersion function in src/DLSSVersion.psm1 (depends on T010)
- [ ] T013 [US1] Export Get-DLSSVersions and Get-DLSSLatestVersion cmdlets in module manifest
- [ ] T014 [US1] Add verbose output and error handling for missing folders

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently

---

## Phase 4: User Story 2 - Upgrade Release DLSS to Latest Staging (Priority: P2)

**Goal**: Allow users to automatically upgrade their Release DLSS to the latest Staging version

**Independent Test**: Run Test-DLSSUpgrade or Invoke-DLSSUpgrade with -WhatIf and verify it shows correct plan to copy files

### Tests for User Story 2

- [ ] T015 [P] [US2] Write Pester test for Upgrade-DLSS function (mock file operations)
- [ ] T016 [P] [US2] Write Pester test for version comparison logic

### Implementation for User Story 2

- [ ] T017 [P] [US2] Implement Compare-DLSSVersions function for semantic version comparison
- [ ] T018 [US2] Implement Get-LatestStagingVersion function in src/DLSSVersion.psm1
- [ ] T019 [US2] Implement Copy-DLSSFiles function to copy DLLs from staging to release
- [ ] T020 [US2] Implement Invoke-DLSSUpgrade function in src/DLSSVersion.psm1 (depends on T017, T018, T019)
- [ ] T021 [US2] Implement Test-DLSSUpgrade function for WhatIf mode
- [ ] T022 [US2] Export Invoke-DLSSUpgrade and Test-DLSSUpgrade cmdlets in module manifest

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently

---

## Phase 5: User Story 3 - Package and Distribute the Tool (Priority: P3)

**Goal**: Make the tool distributable via PowerShell Gallery, Scoop, and winget

**Independent Test**: Install the module from each distribution channel and verify cmdlets work identically to source

### Implementation for User Story 3

- [ ] T023 [P] [US3] Create PowerShell Gallery package metadata in src/DLSSVersion.psd1 (author, description, tags, etc.)
- [ ] T024 [P] [US3] Create Scoop bucket manifest (dlss-version-toolkit.json) in a distribution folder
- [ ] T025 [P] [US3] Create winget package manifest (manifests/d/DLSSVersionToolkit/) for winget
- [ ] T026 [US3] Create CLI wrapper script for non-module usage (check-dlss-versions.ps1 as distributed binary)
- [ ] T027 [US3] Test installation from PowerShell Gallery (Publish-Module)
- [ ] T028 [US3] Test Scoop installation from local bucket

---

## Phase 6: User Story 4 - Multi-Component Version Reporting (Priority: P4)

**Goal**: Display versions for all DLSS-related components (DLSS, Frame Gen, DLSSD, DeepDVC)

**Independent Test**: Run Get-DLSSVersions and verify all available components are displayed

### Implementation for User Story 4

- [ ] T029 [P] [US4] Extend Get-ComponentVersion to support dlssd and deepdvc components
- [ ] T030 [US4] Update Get-DLSSVersions output to include all available components
- [ ] T031 [US4] Update version table display to show DLSSD and DeepDVC columns when present

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [ ] T032 [P] Update README.md with usage instructions and examples in C:\Users\jolti.PHANERON\dlss-version-toolkit\README.md
- [ ] T033 Run Pester tests and fix any failures
- [ ] T034 [P] Add help documentation (Get-Help content) for all exported cmdlets
- [ ] T035 Validate upgrade functionality works correctly end-to-end (on a test system)
- [ ] T036 Clean up and refactor any duplicate code in the module

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion
  - User stories can then proceed in parallel (if staffed)
  - Or sequentially in priority order (P1 → P2 → P3 → P4)
- **Polish (Final Phase)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P2)**: Can start after Foundational (Phase 2) - May integrate with US1 but should be independently testable
- **User Story 3 (P3)**: Can start after Foundational (Phase 2) - Independent from US1/US2 implementation
- **User Story 4 (P4)**: Can start after Foundational (Phase 2) - Depends on US1 core functionality

### Within Each User Story

- Tests (if included) MUST be written and FAIL before implementation
- Helper functions before main cmdlets
- Core implementation before export/interface
- Story complete before moving to next priority

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel
- All Foundational tasks marked [P] can run in parallel (within Phase 2)
- Once Foundational phase completes, all user stories can start in parallel (if team capacity allows)
- All tests for a user story marked [P] can run in parallel
- Different user stories can be worked on in parallel by different team members

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Test User Story 1 independently
5. Deploy/demo if ready - this is the MVP

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Deploy/Demo (MVP!)
3. Add User Story 2 → Test independently → Deploy/Demo
4. Add User Story 3 → Test independently → Deploy/Demo
5. Add User Story 4 → Test independently → Deploy/Demo
6. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1 (P1 - MVP)
   - Developer B: User Story 2 (P2 - Upgrade)
   - Developer C: User Story 3 (P3 - Distribution)
3. Stories complete and integrate independently

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Verify tests fail before implementing
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Avoid: vague tasks, same file conflicts, cross-story dependencies that break independence
- The existing check-dlss-versions.ps1 script should NOT be modified - it serves as the reference implementation
