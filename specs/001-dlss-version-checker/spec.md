# Feature Specification: DLSS Version Toolkit

**Feature Branch**: `001-dlss-version-checker`
**Created**: 2026-04-20
**Status**: Draft
**Input**: User description: "Windows PowerShell tool for checking and upgrading NVIDIA DLSS versions on a user's system. The goal is to make it distributable so others can use it."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Check Installed DLSS Versions (Priority: P1)

As a PC gamer using NVIDIA DLSS override feature, I want to check which DLSS versions are currently installed on my system so that I can verify I have the latest version available.

**Why this priority**: This is the core MVP functionality that enables users to inspect their current DLSS installation. Without this, the tool has no value. Every user who wants to check or upgrade their DLSS needs this capability first.

**Independent Test**: Can be fully tested by running the tool without the -Upgrade flag and verifying it displays all DLSS versions from both Release and Staging locations with correct version numbers extracted from nvngx_package_config.txt files.

**Acceptance Scenarios**:

1. **Given** DLSS Release is installed, **When** user runs the tool, **Then** display all Release folder DLSS versions with Build ID, DLSS version, and FrameGen version
2. **Given** DLSS Staging is installed, **When** user runs the tool, **Then** display all Staging folder DLSS versions with Build ID, DLSS version, and FrameGen version
3. **Given** Both Release and Staging DLSS are installed, **When** user runs the tool, **Then** show combined list from both locations with clear Location column indicating "Release" or "Staging"
4. **Given** No DLSS is installed at all, **When** user runs the tool, **Then** display appropriate message indicating no DLSS versions found without errors

---

### User Story 2 - Upgrade Release DLSS to Latest Staging (Priority: P2)

As a PC gamer who wants the latest DLSS improvements, I want to automatically upgrade my Release DLSS to match the latest Staging version so that I can benefit from the newest DLSS features and bug fixes.

**Why this priority**: This is the primary value-add feature that goes beyond simple inspection. It allows users to easily update their DLSS without manually copying files. This is what makes the tool useful beyond just a viewer.

**Independent Test**: Can be fully tested by running the tool with -Upgrade flag and verifying it copies the latest staging DLLs and config to the Release location, then confirming the version display reflects the upgrade.

**Acceptance Scenarios**:

1. **Given** Latest Staging version is higher than Release version, **When** user runs tool with -Upgrade flag, **Then** copy nvngx_dlss.dll and nvngx_dlssg.dll from staging to release location
2. **Given** Latest Staging version is higher than Release version, **When** user runs tool with -Upgrade flag, **Then** copy nvngx_package_config.txt from staging to release location
3. **Given** No Staging versions are installed, **When** user runs tool with -Upgrade flag, **Then** display message indicating no staging versions available for upgrade
4. **Given** Staging version is older or equal to Release version, **When** user runs tool with -Upgrade flag, **Then** display message indicating release is already up to date

---

### User Story 3 - Package and Distribute the Tool (Priority: P3)

As a tool developer, I want to distribute this DLSS Version Toolkit so that other PC gamers can easily use it without manual setup, through popular Windows package managers.

**Why this priority**: This transforms the tool from a personal script to a distributable product. Distribution via PowerShell Gallery, Scoop, and winget makes it accessible to users who are not comfortable running scripts from GitHub. This expands the potential user base significantly.

**Independent Test**: Can be fully tested by installing the tool from each distribution channel and verifying the commands work as expected: Get-DLSSVersions, Test-DLSSUpgrade, and the CLI interface function identically to the source version.

**Acceptance Scenarios**:

1. **Given** Tool is packaged as PowerShell Module, **When** user runs `Install-Module -Name DLSSVersion`, **Then** module installs and provides Get-DLSSVersions, Test-DLSSUpgrade cmdlets
2. **Given** Tool is packaged as PowerShell Module, **When** user runs `scoop install dlss-version-toolkit`, **Then** Scoop installs the module and creates CLI wrapper
3. **Given** Tool is packaged for winget, **When** user runs `winget install DLSSVersionToolkit`, **Then** winget installs the tool and adds to PATH
4. **Given** Module is installed, **When** user runs `Get-Command -Module DLSSVersion`, **Then** shows all exported cmdlets with correct syntax

---

### User Story 4 - Multi-Component Version Reporting (Priority: P4)

As a PC gamer who uses multiple NVIDIA DLSS features, I want to see versions for all DLSS-related components (DLSS, Frame Gen, DLSSD, DeepDVC) so that I can verify all components are up to date.

**Why this priority**: This provides comprehensive visibility into all NVIDIA NGX components. Advanced users who use Frame Generation or DeepDVC want to track all these versions. It future-proofs the tool for additional components.

**Independent Test**: Can be fully tested by checking the tool output includes all available components with their respective versions when present on the system.

**Acceptance Scenarios**:

1. **Given** DLSS (nvngx_dlss.dll) is installed, **When** tool runs, **Then** display DLSS version from nvngx_package_config.txt
2. **Given** DLSS Frame Gen (nvngx_dlssg.dll) is installed, **When** tool runs, **Then** display FrameGen version
3. **Given** DLSSD (nvngx_dlssd.dll) is installed, **When** tool runs, **Then** display DLSSD version if available in config
4. **Given** DeepDVC is installed, **When** tool runs, **Then** display DeepDVC version if available in config

---

### Edge Cases

- What happens when the NVIDIA NGX folders do not exist at all (fresh Windows install)?
- How does system handle corrupted or malformed nvngx_package_config.txt files?
- What when multiple version folders exist in Release or Staging locations (older and newer versions)?
- How does the system handle permission denied errors when trying to copy files during upgrade?
- What when staging DLLs have different names or structure than expected?
- How does the tool handle very old Windows 10 systems with limited PowerShell capabilities?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST scan both Release (`C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\`) and Staging (`C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\`) directories to find all installed DLSS versions
- **FR-002**: System MUST extract DLSS version from nvngx_package_config.txt file by matching pattern "dlss, X.X.X" and FrameGen version by matching "dlssg, X.X.X"
- **FR-003**: System MUST display results in a formatted table with columns: Location, Build ID, DLSS, FrameGen
- **FR-004**: System MUST identify and display the latest available DLSS version across all locations
- **FR-005**: System MUST support -Upgrade flag to copy latest staging DLLs to release location when staging version is newer
- **FR-006**: System MUST copy both the DLL files (nvngx_dlss.dll, nvngx_dlssg.dll) and nvngx_package_config.txt during upgrade
- **FR-007**: System MUST work on PowerShell 5.1 without using modern PowerShell 7+ syntax (no ternary operator, no null-coalescing)
- **FR-008**: System MUST provide appropriate error messages when paths do not exist or access is denied
- **FR-009**: System MUST support version comparison using [version] type for semantic sorting
- **FR-010**: System MUST be packageable as a PowerShell Module with proper .psd1 manifest
- **FR-011**: System MUST export cmdlets: Get-DLSSVersions, Get-DLSSLatestVersion, Test-DLSSUpgrade (or similar names)
- **FR-012**: System MUST provide CLI interface for non-module usage (direct script execution)

### Key Entities

- **DLSSVersion**: Represents an installed DLSS version with Location (Release/Staging), BuildID, DLSS version string, FrameGen version string
- **DLSSComponent**: Represents individual DLSS components (dlss, dlssg, dlssd, deepdvc) with their respective versions from config file
- **UpgradeOperation**: Represents an upgrade action with source staging version, target release version, and result status

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can run the tool and see their installed DLSS versions in under 5 seconds on typical hardware
- **SC-002**: Users can successfully upgrade their Release DLSS to the latest Staging version with a single command
- **SC-003**: 100% of users can install the tool via PowerShell Gallery with `Install-Module -Name DLSSVersion` and have it work immediately
- **SC-004**: Tool runs successfully on Windows 10 (1903+) and Windows 11 with PowerShell 5.1
- **SC-005**: All edge cases (missing folders, corrupted configs, permission errors) are handled gracefully with informative messages

## Assumptions

- Users have NVIDIA GPU drivers installed that support DLSS override feature
- Users have administrative permissions to read NVIDIA NGX folders and perform upgrades
- The NVIDIA NGX folder structure remains consistent with the expected paths under ProgramData
- Users are comfortable running PowerShell scripts with appropriate execution policy
- Distribution via PowerShell Gallery, Scoop, and winget meets the primary distribution needs for Windows users
- No internet connectivity is required for the core functionality (local file inspection only)
- Upgrade feature requires the Staging folder to already contain newer DLSS versions (tool does not download from NVIDIA)
