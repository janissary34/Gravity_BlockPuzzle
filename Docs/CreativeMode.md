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
| `CreativeTakeConfig` (SO) | Level, UI profile, background mode/colour, timer mode, booster counts, capture size/fps, tail frames, safety limit | Immutable config |
| `CreativeUIProfile` (SO) | Per `CreativeUIGroup`: hidden during the take, or left to the game | Reversible runtime toggles only |
| `CreativeSceneBindings` (scene component) | Explicit references: canvases/objects per UI group, backdrop renderers | Assigned on `RuntimePieceFactoryBootstrap.creativeSceneBindings`; no runtime search |
| `CreativeBoosterInventory` | In-memory booster counts for the take | Injected through `BoosterInventoryRuntime.Configure`; never touches PlayerPrefs |
| `GravityLevelRuntime.ConfigureLevelOverride` | Which level is built | Explicit API; must run before `FindLevelToPlay` |
| `CreativeSession` (plain C#) | Mutable take state, event log, take end | Observes the board and inventory events. The only gameplay effect is an optional timer pause through the board's owner-scoped `TryPauseTimer` |
| `CreativeEventLog` | `<take>.events.json` | Schema `creative-pipeline.events/1` |

**Lifecycle:**
1. Bootstrap `Awake`:
   - consume the request;
   - configure the creative inventory and the level override;
   - create the session after the pools are ready.
2. Bootstrap `Start`: `StartForActiveScene()`, then `session.Begin()`. `Begin` subscribes to events, applies the background and UI profile, and logs `level.ready`.
3. On `LevelCleared` or `LevelFailed`: wait `tailFrames` frames, then `End()`. `End` unsubscribes, resumes the timer pause and writes `events.json`; then the editor leaves Play Mode, which finalises the recording.
4. A safety limit (`maxSeconds` of game time) ends a take that never reaches a result.
5. `OnDestroy` also calls `End()`. It is idempotent.

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

**Not yet logged** (they need new events at the existing haptic call sites; they wait for the independent review because some are in `PuzzleDragController`): valid/invalid drop, cutter contact, Hammer/Rocket impact, freeze start.

## Capture

Capture uses Unity Recorder (`com.unity.recorder` 4.0.3, added on this branch), started at Play Mode start by an editor-only driver. The driver arrives in the next step, once the package is resolved and its API can be compile-checked. Recording starts before Scene1 builds its level, so the Game View is already at the capture size (default 1080×1920 @ 30 fps) when the camera fit is computed. The take therefore includes the splash; the pipeline trims to `level.ready`.

## Pending (needs the independent review per AGENTS.md)

- Scripted pointer input: a narrow injected pointer source handled next to the existing touch/mouse paths in `PuzzleDragController`, reusing select/drag/release so every rule applies.
- New gameplay-moment events at the haptic call sites.
