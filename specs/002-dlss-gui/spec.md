# DLSS Version Toolkit — WPF GUI Edition

**Feature Branch**: `002-dlss-gui`  
**Created**: 2026-05-08  
**Status**: Draft  
**Input**: User description: "Add a WPF GUI to the DLSS Version Toolkit as a single .exe, with rewrite of core logic in C#, framework-dependent .NET 9 deployment, advanced features (custom paths, export, tray, auto-scan), GUI replaces existing PS scripts."

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View Installed DLSS Versions (Priority: P1)

As a PC gamer using NVIDIA DLSS override, I want to see all installed DLSS versions in a visual dashboard so that I can quickly understand my current DLSS setup without using the command line.

**Why this priority**: This is the core viewing capability. A GUI dashboard is the primary way users interact with the tool. Without this, the GUI has no value.

**Independent Test**: Can be fully tested by launching the app, verifying it scans all sources (NGX Release, NGX Staging, AnWave, Streamline SDK), and displays them in the version table with correct data.

**Acceptance Scenarios**:

1. **Given** the app launches, **When** main window opens, **Then** display a dashboard with all detected DLSS versions across all sources (Release, Staging, AnWave, Streamline SDK) in a formatted table with columns: Source, Build ID, DLSS, Frame Gen, DLSSD, DeepDVC, Streamline
2. **Given** NGX Release is installed, **When** dashboard loads, **Then** show Release versions with Build ID, DLSS version, and FrameGen version
3. **Given** NGX Staging is installed, **When** dashboard loads, **Then** show Staging versions alongside Release
4. **Given** AnWave (dlssglom) is installed, **When** dashboard loads, **Then** show AnWave versions from DLL metadata with Source labeled "AnWave"
5. **Given** Streamline SDK is installed, **When** dashboard loads, **Then** show Streamline SDK versions from DLL metadata with Source labeled "Streamline SDK"
6. **Given** No DLSS is installed at all, **When** dashboard loads, **Then** display "No DLSS versions detected" message without errors
7. **Given** A source has mixed version components (some known, some unknown), **When** dashboard loads, **Then** display "Unknown" for missing components

---

### User Story 2 - Upgrade Release DLSS (Priority: P1)

As a PC gamer who wants the latest DLSS, I want to upgrade my Release DLSS to the latest Staging version with one click so that I can benefit from newer DLSS features and bug fixes without risk.

**Why this priority**: This is the primary value-add beyond simple viewing. It makes the tool actively useful for keeping DLSS current.

**Independent Test**: Can be fully tested by running the upgrade button with a newer Staging version available and verifying: backup created, files copied, table refreshes with new version.

**Acceptance Scenarios**:

1. **Given** Staging version is newer than Release, **When** user clicks "Upgrade Release", **Then** create timestamped backup of Release folder, copy DLSS DLLs and config from Staging to Release, display success message, refresh dashboard
2. **Given** Staging version is newer than Release, **When** user clicks "Upgrade Release", **Then** create backup in `versions\.dlss-backup-<yyyyMMdd-HHmmss>` format, verify backup file count matches source
3. **Given** Upgrade copy operation fails, **When** user clicks "Upgrade Release", **Then** automatically restore from backup, display failure message with details, leave backup in place
4. **Given** No Staging versions are installed, **When** user clicks "Upgrade Release", **Then** display "No staging versions available" message
5. **Given** Release is already up to date with Staging, **When** user clicks "Upgrade Release", **Then** display "Release is already up to date" message
6. **Given** User is not running as Administrator, **When** user clicks "Upgrade Release", **Then** display "Administrator access required" message with instructions

---

### User Story 3 - System Tray and Background Operation (Priority: P2)

As a PC gamer who wants DLSS version monitoring without clutter, I want the app to minimize to the system tray and run in the background so that I can get version notifications without the app taking up space on my taskbar.

**Why this priority**: This makes the tool feel like an always-on monitoring utility rather than a one-shot tool. Users can keep it running passively and get alerts when updates are available.

**Independent Test**: Can be fully tested by minimizing the window, verifying it disappears from taskbar and appears in tray, checking context menu options, and verifying notifications appear.

**Acceptance Scenarios**:

1. **Given** the app is running, **When** user clicks the minimize button, **Then** hide the main window from taskbar and show the app icon in the system tray
2. **Given** the app is minimized to tray, **When** user right-clicks the tray icon, **Then** show context menu with "Show Dashboard", "Check Now", and "Exit" options
3. **Given** the app is minimized to tray, **When** user left-clicks the tray icon, **Then** restore the main window
4. **Given** the app is minimized to tray, **When** user clicks "Exit" in context menu, **Then** close the application completely
5. **Given** the app is minimized to tray, **When** a new DLSS version is detected via background scan, **Then** display a system notification with version details
6. **Given** the app is minimized to tray, **When** user clicks the notification, **Then** restore the main window

---

### User Story 4 - Auto-Scan and Notifications (Priority: P2)

As a PC gamer who wants to know when new DLSS versions are available, I want the app to automatically check for new versions on startup and periodically in the background so that I don't have to remember to manually run the tool.

**Why this priority**: This transforms the tool from an on-demand checker into an always-aware monitor. It keeps users informed without any extra action on their part.

**Independent Test**: Can be fully tested by observing startup scan, waiting for periodic interval, and verifying notifications on new version detection.

**Acceptance Scenarios**:

1. **Given** the app starts, **When** main window opens, **Then** automatically scan all DLSS sources and update the dashboard
2. **Given** the app is running, **When** 4 hours have passed since last scan, **Then** re-scan all sources in the background
3. **Given** a new DLSS version is detected in any source, **When** the periodic scan completes, **Then** show a system tray notification "New DLSS version available: X.X.X.X from [Source]"
4. **Given** no new versions are detected, **When** periodic scan completes, **Then** silently update internal state without notification
5. **Given** the app starts with an outdated DLSS version, **When** startup scan completes, **Then** show a notification "Outdated DLSS detected. Latest: X.X.X.X"
6. **Given** user clicks "Check Now" from tray context menu, **When** user triggers it, **Then** immediately scan all sources and update dashboard or show notification

---

### User Story 5 - Custom Path Configuration (Priority: P2)

As an advanced PC gamer with non-standard DLSS setups, I want to specify custom paths for NGX base directory, AnWave folder, and Streamline SDK so that the tool works with my specific installation configuration.

**Why this priority**: Power users may install AnWave or Streamline SDK in custom locations. Without this, the tool would miss their installations.

**Independent Test**: Can be fully tested by specifying non-default paths in settings, verifying the tool scans those paths, and confirming default paths are used when custom paths are not set.

**Acceptance Scenarios**:

1. **Given** the app has default NGX path, **When** user opens Settings, **Then** display default paths for NGX, AnWave, and Streamline SDK with "Browse" buttons
2. **Given** user clicks "Browse" for NGX path, **When** folder picker opens, **Then** allow selecting a directory and update the path field
3. **Given** user enters a custom AnWave path, **When** user saves settings, **Then** use that path for AnWave scanning instead of auto-detecting
4. **Given** user enters a non-existent custom path, **When** user saves settings, **Then** show a warning that the path does not exist but allow saving
5. **Given** custom paths are configured, **When** dashboard loads, **Then** scan the configured paths instead of defaults
6. **Given** user clears a custom path, **When** user saves settings, **Then** revert to auto-detection for that source

---

### User Story 6 - Export Reports (Priority: P3)

As a PC gamer who wants to share DLSS version info or keep records, I want to export a report of my DLSS versions so that I can share it with others or keep a personal log.

**Why this priority**: This adds value for users who want to track DLSS versions over time or share system specs with others (e.g., for debugging in forums).

**Independent Test**: Can be fully tested by clicking Export, selecting a format and path, verifying the file is created with correct content.

**Acceptance Scenarios**:

1. **Given** versions are displayed in dashboard, **When** user clicks "Export Report", **Then** show save dialog with format options: CSV, JSON
2. **Given** user selects CSV format, **When** user chooses a save path and confirms, **Then** create a CSV file with columns: Source, BuildID, DLSS, FrameGen, DLSSD, DeepDVC, Streamline, timestamp
3. **Given** user selects JSON format, **When** user chooses a save path and confirms, **Then** create a JSON file with full version data and metadata
4. **Given** export is successful, **When** user confirms, **Then** show success message with path to exported file

---

### User Story 7 - Dashboard with Version Comparison and Recommendations (Priority: P1)

As a PC gamer who wants to understand the relationship between all DLSS sources, I want to see a comparison view that highlights which source has the newest version for each component so that I know what needs updating.

**Why this priority**: This ties all sources together into a cohesive picture. Users need to understand not just what versions they have, but which is the newest and what the recommendation is.

**Independent Test**: Can be fully tested by running with all four sources present and verifying the comparison table, newest indicators, and recommendations are all displayed correctly.

**Acceptance Scenarios**:

1. **Given** all sources are present, **When** dashboard loads, **Then** show comparison table with all sources as rows and components as columns
2. **Given** all sources are present, **When** dashboard loads, **Then** highlight the newest version per component (e.g., green text or checkmark)
3. **Given** all sources are present, **When** dashboard loads, **Then** show recommendation section: "To update NGX Release, sync from [Source] which has DLSS [version]"
4. **Given** Release is already newest, **When** dashboard loads, **Then** show "All sources up to date" in the recommendation section
5. **Given** Streamline SDK has newer DLSS than NGX Release, **When** dashboard loads, **Then** show recommendation to update NGX from Streamline SDK

---

### User Story 8 - Sync from Streamline SDK or AnWave (Priority: P2)

As a PC gamer who downloaded the Streamline SDK, I want to sync its newer DLSS versions to my NGX Release so that I can use the latest available DLSS regardless of which source has it.

**Why this priority**: This gives users full control over which source feeds into NGX Release. Streamline SDK often has the newest versions between NVIDIA driver updates.

**Independent Test**: Can be fully tested by having Streamline SDK with newer versions than NGX, clicking sync, and verifying files are copied correctly.

**Acceptance Scenarios**:

1. **Given** Streamline SDK has newer DLSS than NGX Release, **When** user clicks "Sync to NGX", **Then** copy DLSS DLLs and config from Streamline SDK to NGX Release, create backup first
2. **Given** AnWave has newer DLSS than NGX Release, **When** user clicks "Sync to NGX", **Then** copy DLLs from AnWave to NGX Release, create backup first
3. **Given** source is not newer than NGX Release, **When** user clicks "Sync to NGX", **Then** display "Source is not newer than NGX Release" message
4. **Given** source files are missing from selected path, **When** user clicks "Sync to NGX", **Then** display "Required DLLs not found in source path" error
5. **Given** sync fails mid-copy, **When** user clicks "Sync to NGX", **Then** restore from backup and show failure message

---

### Edge Cases

- What happens when the NVIDIA NGX folders do not exist at all (fresh system with no NVIDIA software)?
- How does the system handle corrupted or unreadable configuration files?
- What when multiple version folders exist in Release or Staging locations?
- How does the system handle permission denied errors during upgrade/sync?
- What when Streamline SDK path is specified but contains no recognized DLL files?
- What when AnWave folder contains outdated DLLs mixed with current ones?
- What when a DLL file has no version metadata (empty FileVersion)?
- What when the user closes the app while an upgrade is in progress?
- What when the system goes to sleep and wakes up — does the periodic scan reschedule?
- What when the app is launched multiple times — does it enforce single instance?
- What when NGX folders exist but are junctions/symlinks (reparse points)?
- What when long paths (>260 chars) are encountered?

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST scan NGX Release folder and display all DLSS version folders with component versions parsed from nvngx_package_config.txt
- **FR-002**: System MUST scan NGX Staging folder and display all DLSS version folders with component versions
- **FR-003**: System MUST scan AnWave/dlssglom folder (if configured or auto-detected) and read DLSS component versions from DLL file metadata
- **FR-004**: System MUST scan Streamline SDK folder (if configured or auto-detected) and read DLSS component versions from DLL file metadata
- **FR-005**: System MUST display all detected versions in a formatted table with Source, BuildID, DLSS, FrameGen, DLSSD, DeepDVC, Streamline columns
- **FR-006**: System MUST highlight the newest version per component across all sources
- **FR-007**: System MUST provide one-click upgrade from Staging to NGX Release with automatic backup before writes
- **FR-008**: System MUST provide sync capability from Streamline SDK or AnWave to NGX Release
- **FR-009**: System MUST restore from backup automatically if upgrade/sync fails
- **FR-010**: System MUST support custom paths for NGX base, AnWave, and Streamline SDK via settings
- **FR-011**: System MUST minimize to system tray and run in background when window is closed
- **FR-012**: System tray icon MUST show context menu with "Show Dashboard", "Check Now", "Exit"
- **FR-013**: System MUST automatically scan all sources on app startup
- **FR-014**: System MUST scan all sources every 4 hours in background when running
- **FR-015**: System MUST show system tray notification when a new DLSS version is detected
- **FR-016**: System MUST export version report to CSV or JSON format
- **FR-017**: System MUST enforce single-instance (only one running copy at a time)
- **FR-018**: System MUST handle reparse points (symlinks/junctions) by not following them
- **FR-019**: System MUST handle long paths (>260 chars) via \\?\ prefix
- **FR-020**: System MUST run on Windows 10 1908+ and Windows 11 with .NET 9 runtime installed

### Non-Functional Requirements

- **NFR-001**: Version scan MUST complete in under 5 seconds on typical hardware
- **NFR-002**: App startup (to visible window) MUST complete in under 3 seconds
- **NFR-003**: Upgrade/sync operation MUST show progress indication
- **NFR-004**: Tray icon MUST be visible and distinguishable at 100% scaling
- **NFR-005**: App MUST survive system sleep/wake without losing periodic scan schedule
- **NFR-006**: All UI text MUST be readable at 100%, 125%, and 150% Windows scaling
- **NFR-007**: App MUST log all operations (scan, upgrade, sync, errors) to a log file
- **NFR-008**: App MUST clean up temporary files on exit

### Key Entities

- **DLSSVersionEntry**: Represents a detected DLSS version from any source. Properties: Source (Release/Staging/AnWave/Streamline), BuildID, DLSS, FrameGen, DLSSD, DeepDVC, Streamline, Path, IsNewest per component.
- **UpgradeOperation**: Represents an upgrade/sync operation. Properties: Source, Target, Status (Pending/InProgress/Completed/Failed/RolledBack), BackupPath, ErrorMessage.
- **AppSettings**: Persisted user configuration. Properties: NgxBasePath, AnWavePath, StreamlinePath, ScanIntervalHours, AutoScanEnabled, MinimizeToTray, StartMinimized.
- **ScanResult**: Represents a complete scan of all sources. Properties: Sources (dict of DLSSVersionEntry), NewestPerComponent, Recommendations.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can launch the app and see all installed DLSS versions in under 3 seconds
- **SC-002**: Users can successfully upgrade NGX Release from Staging with one click, with backup created first
- **SC-003**: Users can minimize the app to tray and receive notifications when new versions are detected
- **SC-004**: Users can configure custom paths for all sources and have the app respect those paths
- **SC-005**: Users can export their DLSS version report to CSV or JSON
- **SC-006**: App runs on any Windows 10 1908+ or Windows 11 system with .NET 9 installed
- **SC-007**: All edge cases (missing folders, permission errors, reparse points, long paths) are handled gracefully with user-visible messages

---

## Assumptions

- Users have .NET 9 runtime installed (framework-dependent deployment)
- Users have NVIDIA GPU with DLSS support and NVIDIA App/drivers installed
- Users have admin privileges when performing upgrade/sync operations (app prompts or shows instructions otherwise)
- AnWave and Streamline SDK are optional downloads — app works without them
- Background scan interval of 4 hours is acceptable (no configurable interval in v1)
- Single-instance enforcement is achieved via mutex — if a second instance starts, it brings the existing instance to foreground
- App settings are persisted to %APPDATA%\DLSSVersionToolkit\settings.json
- Log files are written to %APPDATA%\DLSSVersionToolkit\logs\
- Backup folders use the same naming convention as the original PowerShell tool: `.dlss-backup-<yyyyMMdd-HHmmss>`