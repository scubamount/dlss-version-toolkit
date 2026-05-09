# UI Layout: DLSS Version Toolkit WPF GUI

**Feature Branch**: `002-dlss-gui` | **Date**: 2026-05-08

---

## Window Layout

```
┌─────────────────────────────────────────────────────────────────┐
│ [DLSS Version Toolkit]  [— □ ×]  (standard window chrome)     │
├─────────────────────────────────────────────────────────────────┤
│  [Scan Now] [Upgrade Release] [Sync from...] [Export] [⚙]     │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  === Installed DLSS Versions ===                                │
│                                                                  │
│  Source         │ Build ID   │ DLSS    │ FG    │ DLSSD │ DeepDVC│ Streamline│
│  ──────────────┼────────────┼─────────┼───────┼───────┼────────┼───────────│
│  NGX Release   │ 20317442   │ 310.6.0 │ 310.6 │ 310.6 │ 310.6  │ N/A        │
│  NGX Staging   │ 20317696   │ 310.7.0 │ 310.7 │ 310.7 │ 310.7  │ N/A        │
│  Streamline SDK│ (path)     │ 310.7.0 │ 310.7 │ 310.7 │ 310.7  │ 2.11.1.0   │
│  AnWave        │ (path)     │ 310.5.3 │ 310.5 │ 310.5 │ —      │ 2.11.1.0   │
│                                                                  │
│  ▲ = Newest per component (highlighted in green)               │
│                                                                  │
├─────────────────────────────────────────────────────────────────┤
│  === Recommendation ===                                          │
│  Streamline SDK has newer DLSS (310.7.0) than NGX Release       │
│  (310.6.0). [Sync to NGX]  [Details ▼]                         │
│                                                                  │
├─────────────────────────────────────────────────────────────────┤
│  Last scan: 2026-05-08 14:30:22 │ Next scan in: 3h 45m          │
│  [●] Scanning active │ [⚙ Settings]                            │
└─────────────────────────────────────────────────────────────────┘
```

### Layout Regions

1. **Title Bar**: Standard Windows title bar (minimize, maximize, close). App icon and "DLSS Version Toolkit" title.
2. **Toolbar**: Row of action buttons at top. [Scan Now], [Upgrade Release], [Sync from...], [Export], [Settings]. Icon + text for each button.
3. **Main Content**: Scrollable area showing:
   - Section header "=== Installed DLSS Versions ==="
   - DataGrid table with version information
   - Source icons (NGX Release = PC icon, Staging = arrow icon, Streamline = SDK icon, AnWave = globe icon)
4. **Recommendation Bar**: Shows actionable recommendation based on version comparison. Gray background to distinguish from main table.
5. **Status Bar**: Bottom strip with last scan time, next scan countdown, scan status indicator, and settings shortcut.

---

## Settings Dialog

```
┌─────────────────────────────────────────────────┐
│  Settings                                   [×]  │
├─────────────────────────────────────────────────┤
│  Paths                                               │
│  ─────────────                                      │
│  NGX Base Path:                                      │
│  [C:\ProgramData\NVIDIA\NGX          ] [Browse]     │
│                                                      │
│  AnWave Path:                                        │
│  [C:\Users\...\Downloads\nvidiaDlssGlom ] [Browse]  │
│  (Leave empty for auto-detect)                      │
│                                                      │
│  Streamline SDK Path:                               │
│  [C:\Users\...\Downloads\streamline-sdk  ] [Browse] │
│  (Leave empty for auto-detect)                      │
│                                                      │
│  ─────────────                                      │
│  [x] Start minimized to tray                        │
│  [x] Check for updates on startup                   │
│  [x] Enable periodic background scans (every 4h)   │
│                                                      │
│           [Save]  [Cancel]                          │
└─────────────────────────────────────────────────┘
```

---

## System Tray

- **Icon**: DLSS logo or stylized "DVT" letters. Distinct at 16x16 and 32x32.
- **Tooltip**: "DLSS Version Toolkit — [status]"

**Tray Context Menu**:
```
┌──────────────────────────┐
│ • Show Dashboard         │
│ • Check Now              │
│ ─────────────────────    │
│ • Exit                   │
└──────────────────────────┘
```

---

## Notifications

**New Version Detected**:
```
DLSS Version Toolkit
New DLSS version available: 310.7.0.0 from Streamline SDK
[Show] [Dismiss]
```

**Outdated Version on Startup**:
```
DLSS Version Toolkit
Outdated DLSS detected. Latest: 310.7.0.0
[Show] [Dismiss]
```

---

## Color Scheme

- **Primary**: NVIDIA green (#76B900) for accents, highlights, newest indicators
- **Background**: Dark gray (#1E1E1E) for modern feel, matches NVIDIA-style dark UI
- **Surface**: Slightly lighter gray (#2D2D2D) for cards and panels
- **Text**: White (#FFFFFF) for primary, light gray (#AAAAAA) for secondary
- **Success**: Green (#4CAF50) for completed operations, newest version highlights
- **Warning**: Yellow (#FFC107) for attention-needed items
- **Error**: Red (#F44336) for failures
- **Border**: Subtle gray (#3D3D3D) separators

---

## Component States

| Component | Normal | Hover | Active | Disabled |
|-----------|--------|-------|--------|----------|
| Button | Dark surface | Lighter surface | Green border | Gray text, no hover |
| Table Row | Default | Light highlight | Green left border | Grayed out |
| Tray Icon | Normal | — | — | Gray overlay |

---

## Typography

- **Title**: Segoe UI Bold, 14pt
- **Section Header**: Segoe UI Semibold, 12pt
- **Body/Table**: Segoe UI Regular, 11pt
- **Status Bar**: Segoe UI Regular, 10pt
- **Monospace (paths/versions)**: Consolas, 10pt

---

## Key Views

### 1. Main Dashboard (default view on launch)

Displays all detected DLSS versions from all sources in a sortable DataGrid. Shows recommendation bar at bottom. Toolbar with action buttons.

### 2. Settings Dialog (modal)

Semi-transparent overlay behind modal dialog. Clean form layout with labeled fields and Browse buttons.

### 3. Tray-Only Mode (app minimized)

Main window hidden. App runs in tray. Tray icon shows tooltip with current status. Context menu available on right-click.

---

## Responsive Behavior

- **Minimum size**: 700 x 500 pixels
- **Default size**: 900 x 600 pixels
- **Resizable**: Yes, with responsive DataGrid columns that expand/contract
- **Maximize**: Full screen supported
- **On resize**: DataGrid columns resize proportionally; toolbar wraps if needed

---

## Interaction Patterns

- **Scan Now**: Triggers immediate full scan of all sources, updates table and recommendation
- **Upgrade Release**: Shows confirmation dialog, then performs upgrade with progress indicator
- **Sync from...**: Dropdown menu to select source (Streamline SDK or AnWave), then confirm and sync
- **Export**: Opens save file dialog, exports to selected format
- **Settings**: Opens settings dialog modal
- **Tray click (left)**: Restores main window
- **Tray click (right)**: Shows context menu
- **Window close button**: Minimizes to tray (does not exit)
- **Exit from tray menu**: Closes application completely