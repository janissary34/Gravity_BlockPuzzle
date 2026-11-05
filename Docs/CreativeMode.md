# Creative Production Mode (marketing captures)

Branch: `marketing`. Creative Mode records gameplay takes for marketing videos and writes a frame-stamped event log the creative-pipeline editor (`~/Documents/GitHub/creative-pipeline`) uses for edit points. It is **off by default** and changes nothing in normal play or in player builds.

## Activation (explicit, editor only)

1. An editor tool writes a one-shot **take request** to EditorPrefs (`GravityPuzzle.Creative.TakeRequest`): config asset path, take name, output folder and capture size/fps. The tools are the `Gravity Puzzle/Creative/Record Take From Selected Config` menu (`CreativeTakeLauncher`) and, later, the pipeline's job runner.
2. The launcher enters Play Mode.
3. `RuntimePieceFactoryBootstrap.Awake` calls `CreativeTakeRequest.ConsumePending()`, which reads **and deletes** the request.
   - **No request:** everything runs exactly as before.
   - **Player builds:** always no request (the code is compiled out).

## Ownership and flow

| Piece | Owns | Notes |
|---|---|---|
| `CreativeTakeConfig` (SO) | Level, displayed level number, UI profile, background mode/colour, timer mode, booster counts, capture size/fps, capture start delay, tail frames, safety limit | Immutable config |
| `CreativeUIProfile` (SO) | Per `CreativeUIGroup`: hidden during the take, or left to the game | Reversible runtime toggles only |
| `CreativeFramingProfile` (SO) | Board top position and progress bar gap, as fractions of screen height | Optional on the take config; empty keeps the game's framing |
| `CreativeSceneBindings` (scene component) | Explicit references per UI group (UI `Graphic`s, preferred; Canvases; inert objects), backdrop renderers, the progress bar and its canvas | In Scene1 on the `CreativeSceneBindings` object, assigned on `RuntimePieceFactoryBootstrap.creativeSceneBindings`. Authored by a menu tool; no runtime search |
| `CreativeBoosterInventory` | In-memory booster counts for the take | Injected through `BoosterInventoryRuntime.Configure`; never touches PlayerPrefs |
| `GravityLevelRuntime.ConfigureLevelOverride` | Which level is built | Explicit API; must run before `FindLevelToPlay` |
| `GravityLevelRuntime.ConfigureDisplayLevelNumber` | Level number shown in HUD, retry, fail and win labels (`DisplayLevelNumber`) | Presentation only; gameplay gating keeps `CurrentLevelNumber`; nothing is saved |
| `CreativeSession` (plain C#) | Mutable take state, event log, take end | Observes the board and inventory events. The only gameplay effect is an optional timer pause through the board's owner-scoped `TryPauseTimer` (Frozen mode, applied on `PrototypeBoard.TimerStarted`) |
| `CreativeEventLog` | `<take>.events.json` | Schema `creative-pipeline.events/1` |

**Lifecycle:**
1. Bootstrap `Awake`:
   - consume the request;
   - configure the creative inventory and the level override;
   - create the session after the pools are ready.
2. Bootstrap `Start`: `StartForActiveScene()`, then `session.Begin()`. `Begin` subscribes to events, applies the background and UI profile, and logs `level.ready`.
3. `captureStartDelayFrames` (default 3) after `level.ready`, the session logs `capture.ready` and raises the editor-only `CreativeSession.CaptureReady` signal, which starts the recorder.
4. On `LevelCleared` or `LevelFailed`: wait `tailFrames` frames, then `End()`. `End` unsubscribes, resumes the timer pause and writes `events.json`; then the editor leaves Play Mode, which finalises the recording.
5. A safety limit (`maxSeconds` of game time) ends a take that never reaches a result.
6. `OnDestroy` also calls `End()`. It is idempotent.

## Events (v1: existing game events only)

| Type | Source |
|---|---|
| `take.start` | session created |
| `level.ready` | session begun |
| `game.state` | `PrototypeBoard.GameStateChanged` (from/to) |
| `piece.state` | `PrototypeBoard.PieceStateChanged`: piece id, from/to. Dragging = pick-up, Placed = drop/land, Falling, HandoffToPhysics/OpenFreefall, Shredding, Despawned |
| `piece.shredded` | `PrototypeBoard.BlockShredded` |
| `level.cleared`, `level.failed` | `PrototypeBoard` |
| `booster.used` | creative inventory `CountChanged` (booster, remaining) |
| `take.end` | session end |

Each event has `unity_frame` (`Time.frameCount`) and `t` (`Time.time`). With Unity Recorder capturing at a constant frame rate, one Unity frame is one video frame. The recorder driver records the Unity frame at which recording started, so `video_frame = unity_frame - recording_start_unity_frame`.

| `piece.released` | `PuzzleDragController.PieceReleased(pieceId, accepted)`: exactly once per release; `accepted` = valid snap, `false` = restored or failed |
| `script.start` | the scripted pointer was injected (`drags` = count). Script frame 0 is the next frame the controller samples |
| `capture.ready` | the level is visually ready and recording is requested (`captureStartDelayFrames` after `level.ready`) |
| `capture.failed` | pre-recording validation failed; no recording starts |
| `pointer.sample` | one per frame of a scripted drag, as the drag controller received it: `x`, `y` (viewport fractions, y up), `phase` (press, move, release). The pipeline uses these to drive the editable hand overlay. Movement and timing are unchanged |

**Not yet logged:** cutter contact, Hammer/Rocket impact, freeze start. These need events in `BlockShredder` and the boosters.

## Capture

`CreativeRecorderDriver` (`Assets/Editor/Creative/`, editor only) records the take with Unity Recorder (`com.unity.recorder` 4.0.3, added on this branch).

1. **Request:** the driver peeks at the take request (it does not consume it) right after the Play Mode domain reload, before any scene `Awake`. With domain reload disabled, it peeks at `ExitingEditMode` instead.
2. **Waiting:** at `EnteredPlayMode` it subscribes once to `CreativeSession.CaptureReady`, the editor-only static event that bridges the runtime session and the editor assembly.
3. **Recording:** on the signal, the driver unsubscribes and prepares and starts a `RecorderController`:
   - Movie recorder, H.264 MP4, quality High, game audio kept;
   - Game View input at the capture size (default 1080×1920);
   - constant frame rate (default 30), capped, manual record mode.

   The recorder is prepared only then, so none of its helper objects live in the splash scene, which is unloaded on the way to Scene1. The video therefore starts on clean gameplay: the loading cover clears about two frames after `level.ready`.
4. **Stop:** at `ExitingPlayMode`, which `CreativeSession.End` triggers after the tail. The driver then writes `<take>.take.json` (schema `creative-pipeline.take/1`) next to the video and `events.json`.
   - If Play Mode ends before the signal, nothing is recorded, no `take.json` is written, and an error is logged.

**`take.json` fields:**
- capture size and frame rate;
- the Game View size at start;
- `recording_start_unity_frame` and `recording_stop_unity_frame`, plus the rendered-frame count for diagnostics;
- Unity version and recorder settings.

**Frame mapping (measured):** Unity Recorder drops the first frames after `StartRecording` while the Game View settles, and it does not record the stop frame. The pipeline therefore uses the stop frame instead of the start frame:

- `video_origin = recording_stop_unity_frame - video_frame_count`
- `video_frame = unity_frame - video_origin`

On the first take (`gbp_L30_20261104-164645`): start 1, stop 607, 604 frames, so the origin is 3. Two drags and a fall matched the video motion to the frame. `recording_start_unity_frame` is kept as a consistency check: the importer rejects a take whose origin is not 0–10 frames after the start.

**Game View aspect:** the level computes its camera fit once, from the screen aspect, when it builds, and the recorder resizes the Game View only after Play Mode starts. The launcher therefore refuses to start a take unless the Game View already has the capture aspect (±1 %). Set the Game View to 1080×1920 once; the editor layout keeps it.

Events logged before recording started (`take.start`, `level.ready`, `capture.ready`) get negative or near-zero video frames.

## UI visibility

- **Bindings:** run *Gravity Puzzle → Creative → Author Scene1 UI Bindings* once with Scene1 open in Edit Mode (again after UI hierarchy changes). `CreativeSceneBindingsAuthoring` maps each `CreativeUIGroup` to hierarchy paths under `InGameUI`, collects the UI `Graphic` components below them, and saves them on the `CreativeSceneBindings` object. A missing path, or unsaved changes in Scene1, stops the tool without changes. The bindings remain editable in the Inspector.
  - A path lists exact object names, one per hierarchy level, because `Transform.Find` cannot reach `New_Booster/feature_Panel_Dark_bg` (its name contains `/`) and returns only the first of duplicate siblings.
  - Every matching object is bound. `FullscreenSuccess` exists twice (`PrototypeBoard.winPanel` uses the scene-added copy), and the settings, fail, revive and retry panels exist both directly under `InGameUI` and under `New_Panels`. Hiding an unused copy is harmless.
- **Profiles** (`Assets/Creative/UIProfiles/`) list the groups to hide; unlisted groups are left to the game:

| Profile | Hides |
|---|---|
| `CreativeUI_FullHUD` | DebugButtons ("NEXT LEVEL") |
| `CreativeUI_GameplayClean` | DebugButtons, Settings, Retry, Coins, DifficultyBadge, popups, fail/revive panels. Keeps the level label, timer, progress bar, boosters and win feedback |
| `CreativeUI_Minimal` | everything `GameplayClean` hides, plus LevelHeader, Timer and BoosterBar. Keeps the board, shredder and progress bar. The default for standard creatives; copy it and remove a group for a timer or booster creative |
| `CreativeUI_BoardOnly` | every group. UI outside the groups stays visible (`Timer_obj`, `Freeze_Slider`, `Vapor`, ...) |

- **Hiding** disables the bound `Graphic` components. Scripts, coroutines and linked tweens keep running, raycasts pass through, and a popup the game opens later stays invisible, because the enabled state survives `SetActive`. The profile is re-applied on every game-state change.
- **Framing:** `CreativeFramingProfile` is a separate choice from the UI profile.
  - `boardTopViewportInset` overrides the game's board position through `GravityLevelRuntime.ConfigureBoardTopViewportInset` before the level builds, so the camera, backdrop and lower grid stay consistent. Moving the camera after the build would expose the end of the backdrop.
  - `progressBarBoardGap` is the gap between the visible top of the board frame and the bottom of the bar, or of the timer when the UI profile shows it. `End` restores the bar.
  - The frame top comes from the rendered frame sprites. They are full rectangles with transparent padding, so `boardFrameArtPadding` (world units) removes it; 0 is safe but leaves a larger gap.
  - **Pre-recording validation:** placement runs after at least one rendered frame, because a Screen Space - Camera canvas follows its camera only when it renders. The take then checks the result: a bar that overlaps the board, or leaves the safe screen area, or a framed take with missing bindings, logs `capture.failed` and an error, raises no `CaptureReady` and leaves Play Mode, so nothing is recorded. In the Editor's Game View the safe area is the whole view.
  - Input and events are unaffected: world positions do not change, and pointers convert through the live camera.
  - Lower insets rent more lower-grid visual slots. At 0.16 on 9:16, L30 uses 110 of 242; the peak across levels is 234. Exhausting the pool fails the level build.
- **Level label:** `displayLevelNumber` on the take config (0 = automatic). The automatic number is the level's campaign position, e.g. asset `L30` is campaign level 33, because three unnamed levels come earlier in `LevelSequence`.

## Marketing-only levels

`Assets/Creative/BlockOut/` holds the first one: a 9×12 level with 25 pieces, a script that never clears it (`BlockOut_UnresolvedTake`), and a separate proof that it can be cleared (`BlockOut_FairSolutionTake`). It is selected only through a take config and is not in `LevelSequence`, so campaign levels and normal play are unchanged. Black and white pieces use the level's legacy colour, with no palette, so no shared config changes.

An unresolved take uses timer mode `Frozen` with a level time limit above the low-time threshold, and ends at `maxSeconds`. It raises neither `level.failed` nor `level.cleared`.

**Scripted drag timing:** avoid move frame counts that put the drag remainder on an exact half step, because `PuzzleDragController` can freeze there. The Block Out scripts use 25 and 19 frames for a whole-cell move.

## Scripted gameplay

- A `CreativeScript` (SO) lists drags in **board cells**: x from the left column, y from the bottom row; (0, 0) is the centre of the lower-left cell. Each drag has a start frame, a move duration in frames, a from-cell and a to-cell. It is set on the take config.
- When the take begins, `CreativeSession` builds a `CreativePointerScript`. This validates the script: frames ≥ 0, each move ≥ 1 frame, and ≥ 1 frame between a release and the next press. An invalid script is logged as an error and the take continues unscripted.
- After `startDelayFrames`, the session injects the script through `PuzzleDragController.ConfigureScriptedPointerSource`.
- `PuzzleDragController.Update` keeps all its existing guards. When an injected source is active, it calls `ProcessScriptedInput`, which mirrors `ProcessMouseInput` line by line through the existing `TrySelectPiece` / `UpdateDragTarget` / `ReleasePiece`. Every game rule therefore applies, and the touch and mouse paths are unchanged.
- The script is active only until its last release; afterwards device input works again. `OnDisable` and `CreativeSession.End` clear it.
- **Authoring guidance:** leave enough frames after each release for falls and shredding to settle before the next press. A press that selects nothing is logged as `[CreativeInput]`.
- **Review:** independent review, both proposal and implementation, recorded in `Docs/Reviews/2026-11-04-creative-input-review.md`. It ran on `gemini-3.8-flash-high`, an exception the user approved because `gemini-4-argon-high` was unavailable.

## Determinism (measured 2026-11-04)

Two takes of `L30_SmokeTake` (`gbp_L30_20261104-164645`, `gbp_L30_20261104-165145`) were compared by Unity frame, measured from `level.ready`:

- **Startup:** `level.ready` lands one Unity frame apart between runs (7 vs 8). Edit points come from each take's own event log, so this does not matter.
- **Identical (24 of 27 events):** every scripted press, drag and release; the falls it triggers; the physics handoff; and `take.end` (600 frames after `level.ready` in both).
- **±1 frame (3 events):** shredding of one piece and the despawn of the other, after `OpenFreefall`. Physics steps at 50 Hz and capture runs at 30 fps. The fixed-step phase at level start differs between runs (game time 0.133 s vs 0.167 s), so free-fall contacts can land one frame apart.

**Consequences:**
- Each take's own log is exact for that take (a frame-accurate `video_frame`), so editing is unaffected.
- Reproducing a take is exact for scripted input and board logic, and within ±1 frame for free-fall/shredder timing.
