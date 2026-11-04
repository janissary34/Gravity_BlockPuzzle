# ADR-006: Creative Production Mode is an explicit, editor-only, observe-only module

- **Status:** accepted for the `marketing` branch (2026-11-04). The input seam has been reviewed independently (proposal and implementation; see `Docs/Reviews/2026-11-04-creative-input-review.md`).
- **Context:** marketing videos need recorded takes of chosen levels with configurable UI, background and capture, plus a frame-accurate event log for automated editing. Earlier games did this with destructive edits on marketing branches (prefab and SO changes, deleted UI), which could not be switched off or reused.

## Decision

1. **Module:** `Assets/Scripts/Creative/` (namespace `GravityPuzzle.Creative.*`) plus `Assets/Editor/Creative/`. Code stays in Assembly-CSharp, where game code lives; splitting the game into asmdefs first was rejected as too large a refactor for this step.
2. **Activation:** a one-shot editor request (EditorPrefs + AssetDatabase), following the existing level-editor Play Preview precedent. No request means Creative Mode does not exist at runtime. Player builds compile the request reader out.
3. **Dependencies are explicit:** the composition root (`RuntimePieceFactoryBootstrap`) creates the session and passes the board, camera, scene bindings and inventory to it. There are no new singletons and no scene searches. UI and backdrop references are authored on `CreativeSceneBindings`.
4. **Observe-only:**
   - The session subscribes to existing board and inventory events.
   - Presentation changes are reversible runtime toggles; nothing is removed or destroyed.
   - The only gameplay-adjacent effect is the optional timer pause, done through the existing owner-scoped `TryPauseTimer`.
5. **Level selection:** an explicit `GravityLevelRuntime.ConfigureLevelOverride`, set before the first `FindLevelToPlay`.
6. **Inventory:** `CreativeBoosterInventory` implements `IPlayerBoosterInventory` in memory, so takes never change the player's save.
7. **Timing:** events carry `Time.frameCount` and `Time.time`. The recorder records its start frame, so events map exactly to video frames when Recorder captures at a constant rate.

## Consequences

- Normal play is unchanged. Existing files gained small, additive hooks:
  - `RuntimePieceFactoryBootstrap`: an optional serialized field, the request read, inventory selection, session create/begin/end, and the display-level configuration;
  - `GravityLevelRuntime`: the level override, one branch in `FindLevelToPlay`, and `ConfigureDisplayLevelNumber` / `DisplayLevelNumber` (labels only; equal to `CurrentLevelNumber` unless a take overrides it);
  - `PrototypeBoard`: a `TimerStarted` observation event (raised once, after `IsTimerStarted` is set), and the win-panel labels read `DisplayLevelNumber`.
- The editor recorder starts on `CreativeSession.CaptureReady`. This `#if UNITY_EDITOR` static event is the single bridge from the runtime session to the editor assembly; the subscriber is one-shot and is cleared at Play Mode exit.
- UI groups must be bound in the scene once (`CreativeSceneBindings`).
- Scripted input enters through a narrow injected `IBoardPointerSource` in `PuzzleDragController`, processed by the existing select/drag/release code. A new observation event, `PuzzleDragController.PieceReleased`, is raised once per release.
- Cutter contact and booster impacts are not logged yet; they need events in `BlockShredder` and the boosters.
- **Reviews:** the AGENTS.md reviews ran on `gemini-3.8-flash-high`, each a user-approved, one-off exception because `gemini-4-argon-high` is unavailable on the account. Records: `Docs/Reviews/2026-11-04-creative-input-review.md` and `Docs/Reviews/2026-11-04-creative-presentation-review.md`. The project policy is unchanged.
