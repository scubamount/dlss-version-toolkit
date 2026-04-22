# Feature Specification: DLSS Version Toolkit

**Feature Branch**: `001-dlss-version-checker`
**Created**: 2026-04-20
**Status**: Draft
**Input**: User description: "Windows tool for checking and upgrading NVIDIA DLSS versions on a user's system. The goal is to make it distributable so others can use it."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Check Installed DLSS Versions (Priority: P1)

As a PC gamer using NVIDIA DLSS override feature, I want to check which DLSS versions are currently installed on my system so that I can verify I have the latest version available.

**Why this priority**: This is the core functionality that enables users to inspect their current DLSS installation. Without this, the tool has no value. Every user who wants to check or upgrade their DLSS needs this capability first.

**Independent Test**: Can be fully tested by running the tool without upgrade mode and verifying it displays all DLSS versions from both Release and Staging locations with correct version numbers.

**Acceptance Scenarios**:

1. **Given** DLSS Release is installed, **When** user runs the tool, **Then** display all Release folder DLSS versions with Build ID, DLSS version, and FrameGen version
2. **Given** DLSS Staging is installed, **When** user runs the tool, **Then** display all Staging folder DLSS versions with Build ID, DLSS version, and FrameGen version
3. **Given** Both Release and Staging DLSS are installed, **When** user runs the tool, **Then** show combined list from both locations with clear Location column indicating "Release" or "Staging"
4. **Given** No DLSS is installed at all, **When** user runs the tool, **Then** display appropriate message indicating no DLSS versions found without errors

---

### User Story 2 - Upgrade Release DLSS to Latest Staging (Priority: P2)

As a PC gamer who wants the latest DLSS improvements, I want to automatically upgrade my Release DLSS to match the latest Staging version so that I can benefit from the newest DLSS features and bug fixes.

**Why this priority**: This is the primary value-add feature that goes beyond simple inspection. It allows users to easily update their DLSS without manually copying files. This is what makes the tool useful beyond just a viewer.

**Independent Test**: Can be fully tested by running the tool with upgrade mode and verifying it copies the latest staging files to the Release location, then confirming the version display reflects the upgrade.

**Acceptance Scenarios**:

1. **Given** Latest Staging version is higher than Release version, **When** user runs tool with upgrade flag, **Then** copy DLSS files from staging to release location
2. **Given** Latest Staging version is higher than Release version, **When** user runs tool with upgrade flag, **Then** copy configuration file from staging to release location
3. **Given** No Staging versions are installed, **When** user runs tool with upgrade flag, **Then** display message indicating no staging versions available for upgrade
4. **Given** Staging version is older or equal to Release version, **When** user runs tool with upgrade flag, **Then** display message indicating release is already up to date

---

### User Story 3 - Package and Distribute the Tool (Priority: P3)

As a tool developer, I want to distribute this DLSS Version Toolkit so that other PC gamers can easily use it without manual setup, through popular Windows package managers.

**Why this priority**: This transforms the tool from a personal script to a distributable product. Distribution via standard Windows package managers makes it accessible to users who are not comfortable running scripts from GitHub. This expands the potential user base significantly.

**Independent Test**: Can be fully tested by installing the tool from each distribution channel and verifying the commands work as expected.

**Acceptance Scenarios**:

1. **Given** Tool is packaged for distribution, **When** user runs standard install command from primary package manager, **Then** tool installs and provides version checking commands
2. **Given** Tool is packaged for secondary package manager, **When** user installs via that package manager, **Then** the tool installs and creates command shortcuts
3. **Given** Tool is packaged for tertiary package manager, **When** user installs via that package manager, **Then** the tool installs and adds to system PATH
4. **Given** Tool is installed, **When** user runs help command, **Then** shows all available commands with usage instructions

---

### User Story 4 - Multi-Component Version Reporting (Priority: P4)

As a PC gamer who uses multiple NVIDIA DLSS features, I want to see versions for all DLSS-related components (DLSS, Frame Gen, DLSSD, DeepDVC) so that I can verify all components are up to date.

**Why this priority**: This provides comprehensive visibility into all NVIDIA NGX components. Advanced users who use Frame Generation or DeepDVC want to track all these versions. It future-proofs the tool for additional components.

**Independent Test**: Can be fully tested by checking the tool output includes all available components with their respective versions when present on the system.

**Acceptance Scenarios**:

1. **Given** DLSS is installed, **When** tool runs, **Then** display DLSS version from configuration file
2. **Given** DLSS Frame Gen is installed, **When** tool runs, **Then** display FrameGen version
3. **Given** DLSSD is installed, **When** tool runs, **Then** display DLSSD version if available in config
4. **Given** DeepDVC is installed, **When** tool runs, **Then** display DeepDVC version if available in config

---

### User Story 5 - Check Global Override Versions via AnWave (Priority: P2)

As a PC gamer using AnWave (dlssglom) for global DLSS overrides, I want to see the versions of all DLSS and Streamline DLLs in my AnWave folder alongside my NGX versions so that I can compare them and know if my global overrides are up to date.

**Why this priority**: AnWave users need visibility into their global override DLL versions to know if they need updating. This is a natural extension of the version checking feature for a growing user base.

**Independent Test**: Can be fully tested by running the tool with the -GlobalPath parameter pointing to an AnWave installation and verifying it displays all DLL versions from file metadata.

**Acceptance Scenarios**:

1. **Given** AnWave is installed with global override DLLs, **When** user runs tool with -GlobalPath parameter, **Then** display all Global DLL versions (DLSS, FrameGen, DLSSD, DeepDVC, StreamlineSDK) from file metadata
2. **Given** Both NGX and Global overrides are present, **When** user runs tool, **Then** show combined list with Location column indicating "Release", "Staging", or "Global"
3. **Given** AnWave folder does not exist, **When** user runs tool with -GlobalPath, **Then** display warning without errors, still show NGX versions
4. **Given** Global override DLLs have older versions than NGX Release, **When** user runs tool, **Then** show all three locations enabling visual version comparison

---

### Edge Cases

- What happens when the NVIDIA DLSS folders do not exist at all (fresh system with no NVIDIA software)?
- How does the system handle corrupted or unreadable configuration files?
- What when multiple version folders exist in Release or Staging locations (older and newer versions)?
- How does the system handle permission denied errors when trying to copy files during upgrade?
- What when game DLSS files have different versions than expected?
- How does the tool handle systems with limited user permissions?
- What when AnWave folder contains a mix of outdated and current DLLs (partial update)?
- What when DLL file metadata is missing version information (unversioned DLLs)?
- What when the Global path is specified but contains no recognized DLL files?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST scan both Release and Staging directories to find all installed DLSS versions
- **FR-002**: System MUST extract DLSS version from configuration files by matching version patterns and FrameGen version by matching corresponding patterns
- **FR-003**: System MUST display results in a formatted table with columns: Location, Build ID, DLSS, FrameGen
- **FR-004**: System MUST identify and display the latest available DLSS version across all locations
- **FR-005**: System MUST support upgrade flag to copy latest staging files to release location when staging version is newer
- **FR-006**: System MUST copy both the core DLL files and the configuration file during upgrade
- **FR-007**: System MUST work on Windows built-in scripting runtime without requiring additional software installations
- **FR-008**: System MUST provide appropriate error messages when paths do not exist or access is denied
- **FR-009**: System MUST support version comparison for semantic sorting
- **FR-010**: System MUST be packageable for standard Windows software distribution channels
- **FR-011**: System MUST provide discoverable commands for each major function (check versions, find latest, test upgrade eligibility)
- **FR-012**: System MUST support direct execution without prior installation

### Key Entities

- **DLSSVersion**: Represents an installed DLSS version with Location (Release/Staging), BuildID, DLSS version string, FrameGen version string
- **DLSSComponent**: Represents individual DLSS components with their respective versions from configuration files
- **UpgradeOperation**: Represents an upgrade action with source staging version, target release version, and result status

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can run the tool and see their installed DLSS versions in under 5 seconds on typical hardware
- **SC-002**: Users can successfully upgrade their Release DLSS to the latest Staging version with a single command
- **SC-003**: Users can install the tool via standard Windows package managers with a single command and have it work immediately
- **SC-004**: Tool runs on any supported Windows version without requiring additional runtime installations
- **SC-005**: All edge cases (missing folders, corrupted configs, permission errors) are handled gracefully with informative messages

## Assumptions

- Users have NVIDIA GPU drivers installed that support DLSS override feature
- Users have administrative permissions to read NVIDIA DLSS folders and perform upgrades
- The NVIDIA DLSS folder structure remains consistent with the expected paths
- Users are comfortable running command-line tools with appropriate execution permissions
- Distribution via standard Windows package managers meets the primary distribution needs for Windows users
- No internet connectivity is required for the core functionality (local file inspection only)
- Upgrade feature requires the Staging folder to already contain newer DLSS versions (tool does not download from NVIDIA)