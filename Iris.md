# Iris — In-Game Camera Keyframing for FFXIV
### A Standalone Dalamud Plugin

**Project:** Iris — Captura-style in-game camera waypoint system for FFXIV GPose  
**Approach:** Standalone Dalamud Plugin (requires Brio)  
**Date:** March 2026  
**Status:** Design Phase  
**License:** GPL-3.0

---

## 1. What Is Iris?

Iris is a standalone Dalamud plugin that brings a **Warframe Captura-inspired** camera path system to FFXIV's GPose. Instead of requiring Blender or external tools, you create camera paths entirely inside the game:

1. Enter GPose
2. Move your free camera to a position (using your controller)
3. Press a button → **plant a waypoint**
4. Repeat for as many positions as you want
5. Press Play → Iris smoothly animates the camera through all your waypoints

No Blender. No XAT exports. No external pipeline. Just like Captura, but in FFXIV.

---

## 2. UX Reference — Captura Feature Mapping

Iris is consciously modeled after Warframe's Captura Advanced Camera Controls. Here's how the features map:

| Captura Feature | Iris Equivalent |
|---|---|
| Place camera waypoints (up to 200) | Plant waypoints via button press in GPose |
| Duration per waypoint | Per-waypoint duration slider |
| Global speed multiplier during playback | Playback speed control |
| Play / Pause / Stop camera track | Full playback transport controls |
| Cinematic Mode (hide UI, play track) | Hide Iris UI + play on a single button press |
| Interpolate Visual Effects between waypoints | Smooth interpolation of position, rotation, FoV, zoom |
| Free camera movement | Controller-driven FreeCam (via Brio or direct camera hook) |
| Edit / Insert waypoints between existing ones | Waypoint list with insert/reorder support |
| Save / Load settings | Save/load camera paths as `.json` |

**What Iris adds that Captura doesn't have:**
- Export camera paths as `.xcp` for use with Brio's existing Cutscene Control
- Import `.xcp` files from the community (XAT ecosystem)
- Configurable easing types per waypoint (linear, ease in/out, smooth spline)

---

## 3. Architecture Decision — Standalone Plugin (not a Brio fork)

Previous versions of this spec recommended forking Brio. After further analysis, **Iris will be a standalone plugin** that requires Brio as a dependency and communicates with it via IPC where available, but does not share a codebase.

### Why Standalone?

- **Independent release cycle** — Iris can ship and update without waiting on Brio PRs
- **Simpler onboarding** — Users install Iris like any other plugin; no custom Brio build
- **Lower maintenance burden** — No need to rebase against Brio's active development
- **Brio IPC covers what we need** — For camera state reading, we can supplement with direct game struct access (same approach Cammy uses), which doesn't require Brio's source

### What Iris Needs From Brio

| Need | How We Get It |
|---|---|
| Detect GPose state | `ICondition[ConditionFlag.WatchingCutscene]` — Dalamud native |
| Read/write game camera position | Direct `GameCamera*` struct via FFXIVClientStructs |
| Free camera movement | Implement our own using Cammy's approach as reference |
| `.xcp` import/export | Study XAT source (GPL-3.0) for format; implement ourselves |

Brio is listed as a **soft dependency** — Iris works best alongside Brio, but the core camera path features function independently.

---

## 4. Data Model

### 4.1 CameraWaypoint

```csharp
public class CameraWaypoint
{
    public int Index;               // Position in the sequence
    public Vector3 Position;        // World-space camera position
    public Quaternion Rotation;     // Camera orientation
    public float FoV;               // Field of view (radians)
    public float Zoom;              // Zoom distance
    public float Duration;          // Seconds to travel TO this waypoint FROM the previous
    public EasingType Easing;       // How to interpolate into this waypoint
    public string? Label;           // Optional user label ("wide shot", "closeup", etc.)
}

public enum EasingType
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,    // Recommended default — mimics Captura's feel
    Smooth        // CatmullRom spline through surrounding waypoints
}
```

### 4.2 CameraPath

```csharp
public class CameraPath
{
    public string Name;
    public bool Loop;
    public float GlobalSpeedMultiplier;   // 0.25x to 4.0x
    public List<CameraWaypoint> Waypoints;

    // Computed property
    public float TotalDuration => Waypoints.Sum(w => w.Duration);
}
```

### 4.3 Save File Format (JSON)

```json
{
  "version": 1,
  "name": "Opening Shot",
  "loop": false,
  "globalSpeedMultiplier": 1.0,
  "waypoints": [
    {
      "index": 0,
      "position": [1.23, 4.56, 7.89],
      "rotation": [0.0, 0.707, 0.0, 0.707],
      "fov": 0.78,
      "zoom": 5.2,
      "duration": 0.0,
      "easing": "EaseInOut",
      "label": "Start — wide"
    },
    {
      "index": 1,
      "position": [2.0, 4.0, 6.0],
      "rotation": [0.1, 0.7, 0.0, 0.7],
      "fov": 0.60,
      "zoom": 3.0,
      "duration": 3.5,
      "easing": "EaseInOut",
      "label": "Push in"
    }
  ]
}
```

---

## 5. Components

### 5.1 IrisCameraService (core logic)

**Responsibilities:**
- Manage the active `CameraPath`
- Record waypoints from current camera state
- Tick-based playback with interpolation
- Import/export `.xcp` and `.json`

**Key Methods:**
```
PlantWaypoint()              — Capture current camera state, append as new waypoint
DeleteWaypoint(index)        — Remove a waypoint
InsertWaypoint(index)        — Insert a waypoint at position using current camera state
ReorderWaypoint(from, to)    — Drag-reorder waypoints
Play() / Pause() / Stop()    — Playback control
SetSpeed(multiplier)         — Global speed override (0.25x–4.0x)
Tick(deltaTime)              — Per-frame: interpolate and apply camera state
ImportXcp(path)              — Load .xcp file into current path
ExportXcp(path)              — Save current path as .xcp
SaveJson(path)               — Save as editable .json
LoadJson(path)               — Load from .json
```

**Interpolation Logic (per tick):**
1. Find the two waypoints surrounding current playhead
2. Compute normalized `t` between them (0.0 → 1.0) using elapsed time vs duration
3. Apply selected easing curve to `t`
4. Interpolate:
   - Position: `CatmullRom(prev, wpA, wpB, next, t)` when ≥4 points, else `Vector3.Lerp`
   - Rotation: `Quaternion.Slerp(wpA.Rotation, wpB.Rotation, t)` — always Slerp, never Euler
   - FoV: `float Lerp`
   - Zoom: `float Lerp`
5. Write interpolated values directly to game camera struct

### 5.2 IrisControllerService (analog input)

**Purpose:** Read controller analog sticks and drive free camera movement in GPose.

**Input Sources:**
- Primary: Dalamud `IGamepadState` — `LeftStick` and `RightStick` return `Vector2`
- Fallback: Native `InputData.getAxisInput` (Cammy's approach) if IGamepadState conflicts with ImGui nav in GPose

**Default Stick Mapping (rebindable):**
```
Left Stick         → Camera move (forward/back/strafe)
Right Stick        → Camera rotate (pan/tilt)
L2 / R2 (triggers) → Zoom out / Zoom in
L1 + any stick     → Precision mode (0.1x speed)
R1 (hold)          → Fast mode (3x speed)
```

**Configuration:**
```
DeadZone: float         (default 0.15)
SensitivityCurve:       Linear | Quadratic | Cubic (default Quadratic)
InvertY: bool           (default false)
MoveSpeed: float        (default 1.0)
RotateSpeed: float      (default 1.0)
ZoomSpeed: float        (default 1.0)
```

### 5.3 IrisWindow (ImGui UI)

**Layout — modeled after Captura's Advanced Camera Controls:**

```
┌──────────────────────────────────────────────┐
│  🎞 Iris  ·  Opening Shot              [≡] X  │
├──────────────────────────────────────────────┤
│  [⏺ Plant Waypoint]   [🎬 Cinematic Mode]    │
├──────────────────────────────────────────────┤
│  Waypoints                                   │
│  ┌─────────────────────────────────────────┐ │
│  │ #1  Start — wide         → 0.0s  [Edit] │ │
│  │ #2  Push in              → 3.5s  [Edit] │ │
│  │ #3  Close-up             → 2.0s  [Edit] │ │
│  │ #4  Pull back            → 4.0s  [Edit] │ │
│  └─────────────────────────────────────────┘ │
│  [+ Insert Here]  [↑ Move Up]  [↓ Move Down] │
├──────────────────────────────────────────────┤
│  Selected: #2 — Push in                      │
│  Duration: [3.5s]    Easing: [EaseInOut ▼]   │
│  Label:    [Push in_______________]          │
│  [📷 Update to Current Camera]               │
├──────────────────────────────────────────────┤
│  Playback                                    │
│  [⏮] [▶ Play] [⏸] [⏹]   Speed: [1.0x ▼]   │
│  [Loop ☐]   Total: 9.5s                     │
├──────────────────────────────────────────────┤
│  [📂 Load .json]  [💾 Save .json]            │
│  [📥 Import .xcp] [📤 Export .xcp]          │
└──────────────────────────────────────────────┘
```

**Key Interactions:**
- **Plant Waypoint** — saves current camera state as a new waypoint at the end of the list
- **Edit** (per waypoint) — expands the waypoint's details inline
- **Update to Current Camera** — overwrites selected waypoint with current camera state (for adjusting without replanting)
- **Cinematic Mode** — hides all Dalamud UI and plays the path (press ESC or any button to exit)
- **Insert Here** — inserts a new waypoint after selected one, using current camera state

---

## 6. Implementation Plan

### Phase 1 — Controller Camera (MVP, ~1 week)
Smooth analog stick control of the free camera in GPose.

1. Hook into the game's camera struct using FFXIVClientStructs
2. Implement `IrisControllerService` — read `IGamepadState`, apply dead zone + sensitivity curve
3. Wire stick input to camera position and rotation per frame
4. Add basic Iris settings window with dead zone / sensitivity sliders
5. Test: smooth pan, tilt, move, zoom with no drift

### Phase 2 — Waypoint Recording & Playback (~1 week)
Core Captura-like loop: plant → play.

1. `CameraWaypoint` and `CameraPath` data models
2. `PlantWaypoint()` — captures current camera state
3. Waypoint list UI (add, delete, reorder, label)
4. `Tick()` interpolation engine (Lerp/Slerp to start, add easing after)
5. Play/Pause/Stop + speed multiplier
6. Cinematic Mode (hide UI, play path, exit on button press)

### Phase 3 — Per-Waypoint Controls (~3–5 days)
Make each waypoint individually configurable.

1. Per-waypoint duration editing
2. Per-waypoint easing type selector
3. "Update to Current Camera" to adjust waypoints without replanting
4. Insert waypoint mid-sequence
5. CatmullRom spline for smoother paths

### Phase 4 — Save / Load (~3–5 days)
Persistence so paths aren't lost between sessions.

1. JSON serialization/deserialization of `CameraPath`
2. Save/Load UI with file picker
3. Multiple saved paths (list of files in a designated folder)

### Phase 5 — .xcp Interop (~3–5 days)
Import community paths; export for Brio's Cutscene Control.

1. Study XAT source (GPL-3.0) for `.xcp` format
2. `ImportXcp()` — `.xcp` → `CameraPath`
3. `ExportXcp()` — `CameraPath` → `.xcp`
4. Round-trip test; test exported `.xcp` in Brio's Cutscene Control

### Phase 6 — Polish (~1 week)
1. Undo/redo (Ctrl+Z) for waypoint edits
2. Keyboard shortcuts for plant, play, stop
3. Controller button binding for Plant Waypoint (so you never leave the camera)
4. Settings persistence across sessions
5. Error handling (GPose not active, no waypoints, etc.)

---

## 7. Technical Risks & Mitigations

| Risk | Mitigation |
|---|---|
| `IGamepadState` unreliable in GPose with ImGui nav | Fall back to `InputData.getAxisInput` (axis IDs 3, 4, 6) as Cammy does |
| Game camera struct offsets change with FFXIV patches | Use FFXIVClientStructs (auto-updated with Dalamud) — never hardcode offsets |
| Interpolation artifacts (gimbal lock, jumps) | Always use `Quaternion.Slerp` for rotation — never Euler; CatmullRom falls back to Lerp with <4 points |
| `.xcp` format is undocumented | Study XAT source thoroughly; test with community `.xcp` packs before shipping Phase 5 |
| Brio updates breaking IPC | All critical camera functionality uses direct game struct access — Brio IPC is optional enhancement only |

---

## 8. Development Environment Setup

1. **Install prerequisites:**
   - [.NET 8 SDK](https://dotnet.microsoft.com/download)
   - Visual Studio 2022 or JetBrains Rider
   - XIVLauncher + FFXIV (launched at least once so Dalamud initialises)

2. **Bootstrap the plugin:**
   ```bash
   # Use the official Dalamud plugin template
   dotnet new install DalamudPlugin
   dotnet new dalamudplugin -n Iris -o ./Iris
   cd Iris
   ```

3. **Key NuGet references (auto-included by template):**
   - `DalamudPackager` — builds the plugin zip
   - `Dalamud.NET.SDK` — all Dalamud + game struct APIs

4. **Build & test locally:**
   ```bash
   dotnet build
   ```
   Then in-game: Dalamud Settings → Experimental → Dev Plugin Locations → add the `bin/Debug/net8.0-windows` path.  
   Load with `/xlplugins` → "Load Dev Plugin."

5. **Test checklist:**
   - Enter GPose (`/gpose`)
   - Open Iris (`/iris`)
   - Verify controller input moves camera
   - Plant 3 waypoints, press Play, verify smooth movement

---

## 9. Repository & Distribution Setup

> This section covers everything needed to make Iris installable directly from inside FFXIV via Dalamud's plugin installer.

### 9.1 GitHub Repository Structure

```
iris/
├── .github/
│   └── workflows/
│       └── build.yml          ← Auto-build + release on push to main
├── Iris/
│   ├── Iris.csproj
│   ├── Plugin.cs              ← Entry point (IPlugin implementation)
│   ├── Services/
│   │   ├── IrisCameraService.cs
│   │   └── IrisControllerService.cs
│   ├── UI/
│   │   └── IrisWindow.cs
│   ├── Models/
│   │   ├── CameraWaypoint.cs
│   │   └── CameraPath.cs
│   └── Properties/
│       └── launchSettings.json
├── repo.json                  ← Dalamud plugin repository manifest
└── README.md
```

### 9.2 Plugin Manifest (`Iris/Iris.csproj` metadata)

The `.csproj` must include the metadata Dalamud uses for the plugin installer:

```xml
<PropertyGroup>
  <AssemblyName>Iris</AssemblyName>
  <Version>0.1.0.0</Version>
  <PackageId>Iris</PackageId>
  <Authors>YourName</Authors>
  <Description>In-game camera waypoint system for FFXIV GPose — Captura-style camera paths without Blender.</Description>
  <RepositoryUrl>https://github.com/YOUR_USERNAME/iris</RepositoryUrl>
</PropertyGroup>
```

You also need a `Iris.json` metadata file alongside your built DLL:

```json
{
  "Author": "YourName",
  "Name": "Iris",
  "Description": "In-game camera waypoint system for FFXIV GPose.",
  "Punchline": "Plant waypoints. Press play. Cinematic camera paths without Blender.",
  "InternalName": "Iris",
  "AssemblyVersion": "0.1.0.0",
  "RepoUrl": "https://github.com/YOUR_USERNAME/iris",
  "ApplicableVersion": "any",
  "Tags": ["camera", "gpose", "photography", "filmmaking"],
  "DalamudApiLevel": 12,
  "LoadRequiredState": 0,
  "LoadSync": false,
  "CanUnloadAsync": true,
  "ImageUrls": []
}
```

### 9.3 GitHub Actions — Auto Build & Release

Create `.github/workflows/build.yml`:

```yaml
name: Build and Release

on:
  push:
    tags:
      - 'v*'          # triggers on version tags like v0.1.0

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
        with:
          submodules: true

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Build
        run: dotnet build --configuration Release
        working-directory: Iris

      - name: Package
        run: |
          Compress-Archive -Path "Iris/bin/Release/net8.0-windows/Iris.dll","Iris/bin/Release/net8.0-windows/Iris.json" -DestinationPath "Iris.zip"

      - name: Create Release
        uses: softprops/action-gh-release@v2
        with:
          files: |
            Iris.zip
          generate_release_notes: true
```

When you push a tag (e.g. `git tag v0.1.0 && git push origin v0.1.0`), GitHub Actions will automatically build and publish the release with the zip attached.

### 9.4 The Repository Manifest (`repo.json`)

This is the file that Dalamud reads when users add your custom repo. Host this at the root of your GitHub Pages (or just the raw GitHub URL).

```json
[
  {
    "Author": "YourName",
    "Name": "Iris",
    "Description": "In-game camera waypoint system for FFXIV GPose.",
    "Punchline": "Plant waypoints. Press play. Cinematic camera paths without Blender.",
    "InternalName": "Iris",
    "AssemblyVersion": "0.1.0.0",
    "RepoUrl": "https://github.com/YOUR_USERNAME/iris",
    "ApplicableVersion": "any",
    "Tags": ["camera", "gpose", "photography"],
    "DalamudApiLevel": 12,
    "DownloadLinkInstall": "https://github.com/YOUR_USERNAME/iris/releases/latest/download/Iris.zip",
    "DownloadLinkUpdate": "https://github.com/YOUR_USERNAME/iris/releases/latest/download/Iris.zip",
    "DownloadLinkTesting": "https://github.com/YOUR_USERNAME/iris/releases/latest/download/Iris.zip",
    "DownloadCount": 0,
    "LastUpdated": 1700000000
  }
]
```

**Where to host `repo.json`:**  
Enable GitHub Pages on your repo (Settings → Pages → deploy from branch `main`, folder `/`). Your `repo.json` URL will then be:  
`https://YOUR_USERNAME.github.io/iris/repo.json`

### 9.5 How Users Install Iris

Once your repo is live, users follow these steps:

1. Open FFXIV with XIVLauncher
2. In-game: `/xlplugins` → Settings (gear icon) → **Experimental**
3. Under **Custom Plugin Repositories**, paste:  
   `https://YOUR_USERNAME.github.io/iris/repo.json`
4. Click the **+** button, then click **Save**
5. Back in **All Plugins**, search for **Iris** and click Install
6. In-game: type `/iris` to open the Iris window

### 9.6 Releasing Updates

```bash
# 1. Bump version in .csproj and Iris.json
# 2. Update repo.json with new AssemblyVersion + timestamp
# 3. Commit everything
git add .
git commit -m "Release v0.2.0 - add per-waypoint easing"

# 4. Tag the release — this triggers the GitHub Action
git tag v0.2.0
git push origin main
git push origin v0.2.0
```

Dalamud will automatically notify users that an update is available next time they open the plugin list.

---

## 10. Success Criteria

### Phase 1 — Controller Camera
- [ ] Analog sticks move the camera smoothly in GPose with no drift
- [ ] Dead zone is configurable and eliminates stick drift
- [ ] L1 precision mode works reliably
- [ ] Settings persist across sessions

### Phase 2 — Core Waypoint System
- [ ] Plant Waypoint captures current camera state correctly
- [ ] 3+ waypoints play back with smooth interpolation
- [ ] Cinematic Mode hides UI and plays path cleanly
- [ ] Speed multiplier affects playback proportionally

### Phase 3–4 — Per-Waypoint Controls + Save/Load
- [ ] Each waypoint has independently editable duration and easing
- [ ] Paths save and load correctly as `.json`
- [ ] Insert waypoint mid-sequence works without disrupting others

### Phase 5 — .xcp Interop
- [ ] Exported `.xcp` files play correctly in Brio's Cutscene Control
- [ ] Community `.xcp` files import and play in Iris

### Distribution
- [ ] `repo.json` is live and accessible
- [ ] Plugin installs via Dalamud custom repo without errors
- [ ] `/iris` command opens the window
- [ ] GitHub Actions builds and releases automatically on tag push

---

## 11. References

| Resource | URL | Purpose |
|---|---|---|
| Dalamud Plugin Template | https://github.com/goatcorp/SamplePlugin | Bootstrap starting point |
| FFXIVClientStructs | https://github.com/aers/FFXIVClientStructs | Camera struct offsets |
| Brio | https://github.com/Etheirys/Brio | GPose tool (soft dependency) |
| XAT | https://github.com/Etheirys/XAT | .xcp format reference |
| Cammy | https://github.com/UnknownX7/Cammy | Analog input + camera hook reference |
| LightsCameraAction | https://github.com/NeNeppie/LightsCameraAction | GPose camera state reference |
| Dalamud Plugin Guide | https://dalamud.dev/plugin-development/getting-started | Official dev docs |

---

*End of specification. Project name: Iris. Start implementation with Phase 1 (Controller Camera).*
