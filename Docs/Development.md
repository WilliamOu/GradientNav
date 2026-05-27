# Development
## Project Structure
The codebase is organized into five top-level folders under `Scripts/`:

- `Data Managers/` — singleton-style runtime services that persist across scenes (settings, session info, player, logging, trials, mocap, replay, etc.)
- `Scene Managers/` — per-scene controllers that drive the flow of each Unity scene (title, gradient navigation, replay, closing)
- `Player/` — player prefab components: the VR and desktop player controllers, UI references, and the editor/replay "three-perspective" camera rigs used outside the main study scene
- `UI/` — standalone UI widgets and tools attached to scene objects (minimap, 3D map preview, settings rows, replay browser, raw-matrix converter, zip importer)
- `Utilities/` — pure data structures and helpers that don't depend on the manager system (stimulus map strategies, test matrix generators)
- `Interfaces and Templates/` — shared interfaces and reference templates (RNG abstraction, TPP UI template)

### High-Level Architecture
The runtime is built around a single root `MonoBehaviour` called `AppManager` that survives scene loads via `DontDestroyOnLoad`. `AppManager` owns one instance of every manager and exposes them as properties (`AppManager.Instance.Settings`, `AppManager.Instance.Logger`, etc.). Anything anywhere in the project that needs cross-cutting state — current settings, the current participant, the current trial, the player prefab, the log buffer — goes through `AppManager.Instance`.

Managers come in two flavors:

- **Plain C# classes**, instantiated with `new` in `AppManager.Awake()`. These hold pure data and have no Unity lifecycle. `SettingsManager`, `SessionDataManager`, and `LogManager` are plain classes.
- **`MonoBehaviour` components**, attached to the `AppManager` GameObject with `AddComponent`. These need Unity lifecycle hooks (coroutines, `Awake`, serialized prefab references, etc.). `PlayerManager`, `OrientationManager`, `StimulusManager`, `TrialManager`, `ShadowManager`, `ReplayManager`, and `UtilitiesManager` are all `MonoBehaviour`s.

Scene managers (`TitleSceneManager`, `GradientNavigationSceneManager`, `ReplaySceneManager`, `ClosingSceneManager`) are *not* part of `AppManager`. They live and die with their scene and act as the orchestrators that wire managers together to produce a specific experience. The gradient navigation scene manager is the most substantial — it runs the actual study via the `RunAllTrials()` coroutine, which (1) recenters the player, (2) optionally runs a training phase, and (3) iterates the trial list, calling into `TrialManager` for trial specs, `StimulusManager` to build the map, `PlayerManager` to position and update the player, `OrientationManager` to walk-to-target in VR, and `LogManager`/`ShadowManager` to record data the entire time.

UI components, player components, and utility classes do not own state of their own where it would be sensible to share. They read from and push into the managers via `AppManager.Instance`.

### Data Managers
Each file in `Data Managers/` corresponds to one logical service.

**`AppManager`** — root singleton. Holds the `Instance` static reference, constructs every other manager in `Awake()`, and stores serialized references to prefabs (VR player, desktop player, orientation markers) and input actions (controller triggers and select buttons) so any manager can reach them. Whenever you need anything global, you go through here.

**`SettingsManager`** — manages all long-term, persistent settings (move speed, time-to-seek, money rewards, success threshold, log interval, training toggle, etc.). Settings are stored as a list of `SettingDef` subclasses (`BoolSetting`, `FloatSetting`, `IntegerSetting`, `EnumSetting`), each with a name, description, value, and `OnChanged` callback. The settings UI in the title scene is generated procedurally from this list, so to add a new setting you (1) add a public cached field, (2) register a `SettingDef` in `InitializeDefaultSettings()`, and (3) map the value in `UpdatedCachedValues()`. See the comment header inside the file for the exact instructions.

**`SessionDataManager`** — holds the data that is unique to one run of the study: participant name, ID, gender, session type (VR or Desktop), and the live state of the current trial (`TrialNumber`, `MapType`, `SpawnPosition`, `GoalPosition`, `State`). Most fields are guarded by `EnsureSessionStarted()` and will throw if accessed before `BeginSession()` is called from the title scene. The `GameState` enum (`Training`, `Idle`, `Orient`, `Paused`, `Trial`) is the canonical state machine used by the gradient navigation scene manager and is also what gets written into the log file every frame.

**`PlayerManager`** — owns the active player instance. `SpawnPlayer()` picks the VR or desktop prefab based on `SessionDataManager.IsVRMode`, instantiates it, and grabs the `PlayerUIReferences` component for downstream access. It also exposes high-level operations — `Teleport`, `TeleportVRToCoordinates`, `SetUIMessage`, `EnableBlackscreen`/`DisableBlackscreen`, `UpdateStimulusUI`, `ToggleMovement` — that the scene managers use without caring whether they're driving a VR rig or a desktop FPS controller. `StimulusIntensity` is the player's current sampled brightness, recomputed each frame from `StimulusManager.GetIntensity()` at the camera's position.

**`StimulusManager`** — the math of the study. `GenerateMap()` builds an `IStimulusMap` (see `Utilities/StimulusStrategies.cs`) from a type index, map size, center offset, and any map-specific parameters (peak list, sigma override, matrix filename). Once built, `GetIntensity(worldPos)` returns the clamped `[0,1]` brightness at any world position. The list of supported map types is `Gaussian`, `Linear`, `Inverse`, `Multi-Peak`, `Torus`, `Linear Multi-Peak`, and `Matrix` — adding a new type means writing a new `IStimulusMap` implementation in `StimulusStrategies.cs` and adding the case in the `GenerateMap()` switch.

**`TrialManager`** — decides which trials to run and in what order. On `Init()`, it picks one of three plan modes (`RandomNoSeed`, `RandomSeeded`, `Csv`) based on settings. CSV trials are loaded from `/Trials` in the persistent path and parsed into `TrialSpec` records, which carry everything the stimulus manager needs to build a map plus the spawn position. The scene manager calls `GetTotalTrialCount()` and `GetTrial(i)` and is otherwise oblivious to where the data came from. `GetTrial(-9999)` returns a default Gaussian spec used during the training phase.

**`OrientationManager`** — coordinates physical-space alignment in VR. `WalkToLocation(x, z)` spawns a red waypoint pillar at the target and only completes the coroutine once the player has stood near it continuously for `walkHoldDelaySeconds`. `LookAtLocation(x, z)` does the same for gaze. The scene manager uses these at the start of every trial (and after every unpause if `ReorientAfterPause` is on) so the participant's real-world position always matches their virtual one before a trial begins.

**`LogManager`** — frame-level data logging. A background `Task` writes batched `LogFrame` records to `XRI.csv` while the main thread captures state into a `ConcurrentQueue` of buffers. The buffer size is configurable in settings (clamped between `MinBufferSize` and `MaxBufferSize`). Each frame records timestamps, headset/controller pose, gaze, the current `GameState`, the current trial number, the current map type, and the current stimulus intensity, among other fields. A two-clock setup (Unity time + `Stopwatch` ticks) is maintained via `UpdateMainThreadTimeMapping()` so that asynchronously-arriving data (notably mocap frames) can be timestamped against the same clock. To add a new logged field, follow the comment header in the file: extend `LogFrame`, populate it in `CaptureState()`, write it in `AppendToRow()`, and add the column to the header in `BeginLogging()`.

**`ShadowManager`** — connects to the Shadow Motion Capture suit via Motion.SDK over TCP, requests the configurable channel stream, and writes a fixed-layout binary file (`Shadow.bin`) with 17-bone pose data per frame. It uses two `Task`s — one ingesting from the network, one writing to disk — joined by a bounded queue, and timestamps each frame against the same stopwatch clock as `LogManager` so mocap data can be aligned against XRI data offline. If the suit is not connected or not running, the file is created but stays empty.

**`ReplayManager`** — drives playback of recorded sessions. `BeginReplayFromFolder()` loads an `XRI.bin`/`Shadow.bin` pair (plus its `settings.json` and `trials.csv` copies) from a `/Replays/<folder>`, spawns visual proxies for the head, hands, gaze direction, and the 17 Shadow mocap dots, then streams them along a scrubbable timeline. The replay UI lives in `Player/ReplayTPPUI.cs`. Axis remapping, head-to-skull offset, and yaw correction are exposed as inspector fields so you can re-align Shadow data to XRI data after the fact.

**`UtilitiesManager`** — a small holder for scene-resident UI helpers (`MinimapRenderer` and `Map3DVisualizer`) that the scene managers need cheap access to. Acts mostly as a convenience accessor — these components live inside the AppManager hierarchy in the scene and are found by `GetComponentInChildren` at `Awake()`.

### Scene Managers
The project has four scenes, each with one scene manager:

**`TitleSceneManager`** — the title screen. Builds the settings menu UI from `SettingsManager.SettingsList`, exposes the participant fields (name, ID, gender), and routes the user to one of three places: the gradient navigation scene (VR or Desktop), the trial creation scene, or a replay (via `ReplaySelectionManager`). The "open file location" button and the "convert raw matrices" button also live here.

**`GradientNavigationSceneManager`** — the main study scene. Holds the central `RunAllTrials()` coroutine described in the high-level overview. Also handles the training phase (`RunTrainingPhase()`), pause/unpause logic, the submission handler (which compares the current stimulus intensity against `SuccessThreshold`, or for matrix maps the distance to the goal against the map's `ScaleParameter`), and end-of-trial accounting (success/failure messages and money rewards). Most actual work is delegated to the data managers; this script's job is sequencing them.

**`ReplaySceneManager`** — a thin scene controller that just calls `AppManager.Instance.Replay.BeginReplayFromFolder()` and does some sanity checks for the Shadow point cloud. The bulk of the replay UI lives on the player prefab itself (`ReplayTPPUI`).

**`ClosingSceneManager`** — the post-study scene. One button: exit.

**`SafetyWallManager`** — not actually a scene manager per se, despite the folder it lives in; it's attached to the safety-wall GameObjects inside the gradient navigation scene. When enabled in settings, it activates four invisible walls that fade in red as the player's head or hands approach the physical boundary of the play area. Reads `SafetyWallRevealRadius` and `SafetyWallHandRevealRadius` from settings.

### Player
The player folder contains scripts attached to the player prefabs (one VR, one desktop) as well as the "three-perspective player" used for non-study scenes.

**`PlayerUIReferences`** — a dumb container that exposes the player camera, gradient `Image`, UI canvas, blackscreen, character controller, continuous-move provider, and static text. `PlayerManager` finds this on the spawned prefab so it can drive the player without hard-wiring references to specific GameObjects.

**`DesktopPlayerControls`** — WASD + mouse-look character controller for the desktop study mode. Pulls move speed and mouse sensitivity from settings.

**`HandAnimator`** — reads trigger and grip values from VR controllers and feeds them to the hand animator for finger curling.

**`ThreePerspectivePlayer`** — a desktop-only camera rig used in the trial creation scene and the replay scene. Supports Isometric, Top-Down, and First-Person modes, edge scrolling, freeze/unfreeze, and event hooks (`OnFreezeStateChanged`, `OnViewModeChanged`) so attached UI scripts can react to mode changes.

**`DefaultTPPUI`**, **`ReplayTPPUI`**, **`TrialCreationTPPUI`** — the three UI overlays that attach to `ThreePerspectivePlayer` in different scenes. `DefaultTPPUI` is the base controller-rotation/perspective-switch UI; `ReplayTPPUI` adds the playback timeline, frame stepping, speed controls, and auto-align buttons for the replay scene; `TrialCreationTPPUI` is the trial editor — file dropdown, trial list, map type dropdown, sigma/spawn/goal inputs, peak editor, matrix file picker, etc. All three subscribe to events on the underlying `ThreePerspectivePlayer` rather than polling.

### UI
The UI folder is for standalone UI widgets that are not tied to the player prefab.

**`MinimapRenderer`** — generates a heat-gradient minimap texture at the configured resolution, draws the player and goal icons, and lays a fading walk trace on top. Has both a full-refresh path (`RefreshMinimap`) and a fast incremental refresh used by matrix-evolution mode (`RefreshMinimapFast`).

**`Map3DVisualizer`** — generates a vertex-colored 3D mesh of the heatmap for diagnostic/debug viewing. Currently not used during the study itself (the calls to it in `GradientNavigationSceneManager` are commented out).

**`SettingRowUI`** — the row prefab used by the settings menu. Inspects the `SettingDef` it's given and shows either a toggle, a number input, or a dropdown.

**`RawMatrixCsvConverter`** — handler for the "convert raw matrices" button on the title screen. Reads each CSV from `/Raw Matrices`, builds a binary `data.bin` plus a `meta.json` describing scale, frame timing, and interpolation, and writes the pair into `/Matrices/<name>/`. This is what produces the assets that `MatrixMap` consumes at runtime.

**`ReplaySelectionManager`** — populates the replay dropdown on the title screen by scanning the `/Replays` folder, validates that each candidate has the required files, and loads the selected replay's settings and trials into the in-memory managers before switching scenes.

**`ZipImportManager`** — utility for importing zipped replay folders into the persistent path. Has separate handling for desktop, Android, and WebGL.

### Utilities
**`StimulusStrategies`** — defines the `IStimulusMap` interface and every map type implementation (`GaussianMap`, `LinearMap`, `InverseMap`, `MultiPeakMap`, `LinearMultiPeakMap`, `TorusMap`, `MatrixMap`). Each one implements `Evaluate(playerPos) -> float` plus `GetPrimaryTarget()`. `MatrixMap` is the only one that reads from disk (loading the binary matrix produced by `RawMatrixCsvConverter`) and is also the only one with a meaningful `ScaleParameter` (its goal-radius, used for matrix-mode success checks).

**`MatrixTestGenerator`** — editor utility for hand-generating test matrices via a `[ContextMenu]` entry. Useful for verifying matrix playback without going through the raw CSV pipeline.

### Interfaces and Templates
**`IRng`** — abstraction over Unity's `Random` so seeded and unseeded trial generation share the same code path. `UnityRng` defers to `UnityEngine.Random`; `SeededRng` wraps `System.Random` with a known seed.

**`TPPUITemplate`** — a worked example showing how to subscribe to `ThreePerspectivePlayer` events. Not used at runtime; copy from this when writing a new TPP UI script.

### Pipeline Overview
- Data logging pipeline (main-thread capture → buffer → background writer; clock-sync with Shadow)
- Replay pipeline (binary format → ReplayManager streamers → scene proxies → UI scrubbing)
- Trial creation pipeline (TrialCreationTPPUI → TrialSpec → CSV in `/Trials` → TrialManager.Init → StimulusManager.GenerateMap)
- Matrix pipeline (Raw CSV → RawMatrixCsvConverter → data.bin + meta.json → MatrixMap)
- Settings pipeline (SettingDef list → SettingRowUI generation → cached values → on-change callbacks)

## Known Bugs
- The trial creation scene does not display an option to adjust the map center for the matrix map type. If changing a trial from a non-matrix map type to a matrix map type, be sure to center it on 0, 0 before changing the map type to matrix. 
- After viewing a replay, the spawn point indicator sprite may be rotated incorrectly. This is a cosmetic issue with no bearing on functionality. 

## Features in Development
- Per-trial map timers. 