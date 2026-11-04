# Independent Technical Review: Creative Mode Presentation Fixes

**Repository:** Gravity BlockPuzzle (`marketing` branch)  
**Document Reviewed:** `PROPOSAL.md` (Creative Mode presentation fixes)  
**Governing Policy:** `AGENTS.md` (Gravity BlockPuzzle Engineering Contract)  
**Review Type:** Pre-implementation Advisory Review (Read-Only)

---

## Executive Summary

The proposal addresses four real presentation defects observed in marketing take captures (frozen timer leakage, HUD level numbering discrepancy, UI group toggling, and splash screen bleeding into capture frames). The foundational approach—keeping changes isolated to editor tools and the creative harness on the `marketing` branch—complies with the project's requirement to preserve normal runtime gameplay and `main`.

However, the proposal introduces several hidden risks, minor contract violations, and unnecessary abstractions:
1. **Win UI Desynchronization:** Overriding the level display text in `RuntimePieceFactoryBootstrap.UpdateLevelDisplays()` misses the win panel's `completedLevelText` in [`PrototypeBoard`](review-context/PrototypeBoard.excerpt.cs#L20-L36), which will display the campaign sequence index during the take's win sequence.
2. **`SetActive(false)` Side Effects on UI:** Deactivating GameObjects kills DOTween tweens linked with `KillOnDisable`, aborts running Coroutines on those components, and fails to suppress popups shown dynamically during gameplay.
3. **Static Delegate Lifecycle Leak:** The editor-only bridge signal does not define unsubscription semantics, causing delegate leaks across Play Mode sessions when domain reload is disabled.
4. **Unnecessary Class Abstractions:** Introducing [`CreativeTimerFreeze`](review-context/PROPOSAL.md#L15-L25) and [`CreativeCaptureSignal`](review-context/PROPOSAL.md#L88-L98) creates single-purpose wrapper classes that conflict with the contract’s mandate for simple, readable solutions over unnecessary abstractions.

---

## Detailed Findings

### Finding 1: Win Panel Text Desynchronization on Level Clear
* **Severity:** High
* **Affected Components:** [`RuntimePieceFactoryBootstrap.UpdateLevelDisplays`](review-context/RuntimePieceFactoryBootstrap.excerpt.cs#L159-L173), [`PrototypeBoard.completedLevelText`](review-context/PrototypeBoard.excerpt.cs#L32-L34), [`PrototypeBoard.nextLevelButtonLabel`](review-context/PrototypeBoard.excerpt.cs#L26-L28)
* **Concrete Failure Scenario:** A take is configured for Level 30 with `displayLevelNumber = 30`. During gameplay, the HUD shows "Level 30". When the board is cleared, the take records `tailFrames` (90 frames) of the win sequence (`GameplayClean` keeps `WinPanel: FullscreenSuccess`). When the win panel opens, [`PrototypeBoard`](review-context/PrototypeBoard.excerpt.cs#L20-L36) populates `completedLevelText` directly from [`GravityLevelRuntime.CurrentLevelNumber`](review-context/GravityLevelRuntime.excerpt.cs#L66-L68) (33), causing a visible contradiction between the HUD ("Level 30") and the win fanfare card ("Level 33 Completed") on the video's climax frame.
* **Recommended Change:** Either:
  1. Have [`RuntimePieceFactoryBootstrap.UpdateLevelDisplays()`](review-context/RuntimePieceFactoryBootstrap.excerpt.cs#L159-L173) (or an explicit initialization method on `PrototypeBoard`) assign `displayedLevelNumber` to `PrototypeBoard` for use when rendering `completedLevelText` and `nextLevelButtonLabel`; or
  2. Provide an explicit presentation property `GravityLevelRuntime.DisplayedLevelNumber` (defaulting to `CurrentLevelNumber`, but overridable via `ConfigureLevelOverride` / `CreativeTakeConfig`) that all UI elements, including `PrototypeBoard`'s win panel, query for display strings.

---

### Finding 2: UI Deactivation Side Effects (`SetActive` vs. `Canvas.enabled`)
* **Severity:** High
* **Affected Components:** [`CreativeSession.ApplyUIProfile`](review-context/CreativeSession.cs#L144-L176), [`CreativeSceneBindings`](review-context/CreativeSceneBindings.cs#L12-L35)
* **Concrete Failure Scenario:**
  1. *Tween Destruction:* `AGENTS.md` mandates `SetLink(gameObject, LinkBehaviour.KillOnDisable)`. Toggling `GameObject.SetActive(false)` on panels or buttons (such as `Settings_Panel`, `New_Booster_Panel`, or `Boosters`) invokes `OnDisable()`, instantly killing all linked tweens on those hierarchies. If any reusable tween sequence was configured with `SetAutoKill(false)`, it is aborted and invalidated.
  2. *Coroutine Termination:* Any coroutine running on a `MonoBehaviour` attached to an object deactivated via `SetActive(false)` is immediately aborted by Unity and will not resume if reactivated.
  3. *Leaking In-Game Popups:* [`CreativeSession.ApplyUIProfile`](review-context/CreativeSession.cs#L144-L176) only runs on `Begin()`, `OnGameStateChanged`, and `FinishAfterTail()`. If game logic activates an unsuppressed popup during `GameState.Playing` without triggering a game-state transition, the popup remains visible on screen for the rest of the take.
* **Recommended Change:**
  - For UI groups containing independent canvas hierarchies, prefer disabling `Canvas.enabled = false` instead of `GameObject.SetActive(false)`. This suppresses rendering and raycasts without triggering `OnDisable`, without killing tweens, and without aborting coroutines.
  - Where a local `Canvas` is absent, use a `CanvasGroup` (`alpha = 0`, `blocksRaycasts = false`, `interactable = false`).
  - Reserve `GameObject.SetActive(false)` strictly for leaf elements that have no scripts or coroutines (e.g. `UI_Level/level_txt_bg (1)`).

---

### Finding 3: Static Delegate Lifecycle and Request Leak in `CreativeRecorderDriver`
* **Severity:** High
* **Affected Components:** [`CreativeRecorderDriver`](review-context/CreativeRecorderDriver.cs#L22-L56), Proposed `CreativeCaptureSignal`
* **Concrete Failure Scenario:**
  - In `CreativeRecorderDriver.OnPlayModeStateChanged`, `pending` was previously peeked and then cleared on `EnteredPlayMode`. If `StartRecording` is deferred until `CaptureReady`, `pending` must be preserved until the signal arrives.
  - If `CreativeRecorderDriver` subscribes to a static `CaptureReady` event:
    1. If the level encounters an exception in `Awake()` or Play Mode is exited early, `CaptureReady` never fires. In `ExitingPlayMode`, `pending` remains uncollected, and the static event retains its delegate subscription.
    2. In environments where "Domain Reload on Enter Play Mode" is disabled, or across consecutive editor runs, the static event subscription is not automatically purged. When the next take starts, multiple duplicate handler delegates fire, causing multiple concurrent calls to `StartRecording()`.
* **Recommended Change:**
  - Ensure the handler in `CreativeRecorderDriver` is strictly one-shot: immediately unsubscribe inside the callback.
  - In `OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode)`, explicitly unsubscribe from `CaptureReady` and nullify `pending`.
  - Log an error in `StopRecording()` if `pending != null` upon exiting Play Mode, confirming that no capture signal was received.

---

### Finding 4: Unnecessary Abstractions Violating Simplicity Principles
* **Severity:** Medium
* **Affected Components:** Proposed `CreativeTimerFreeze`, Proposed `CreativeCaptureSignal`
* **Contract Reference:** `AGENTS.md` ("Engineering principles: Prefer simple, readable solutions over unnecessary abstraction"; "Avoid classes that provide no meaningful abstraction").
* **Concrete Failure Scenario:**
  - The proposal introduces a standalone class `CreativeTimerFreeze` to subscribe to `board.TimerStarted` and call `board.TryPauseTimer(this)`. However, [`CreativeSession`](review-context/CreativeSession.cs#L22-L70) is *already* the dedicated plain C# lifecycle owner for the take, already holds `board`, already subscribes to board events, and already calls `board.ResumeTimer(this)` in `End()`. Creating a separate class introduces superfluous object allocation and fragmented subscription lifecycle for what is essentially a 4-line event handler.
  - Similarly, creating `CreativeCaptureSignal` as a dedicated static class wrapping an `event Action<string>` and a `Raise` helper is an unnecessary wrapper.
* **Recommended Change:**
  - Eliminate `CreativeTimerFreeze`. Have [`CreativeSession`](review-context/CreativeSession.cs#L55-L100) subscribe directly to `board.TimerStarted` in `Begin()` and unsubscribe in `End()`.
  - Declare `public static event Action<string> CaptureReady;` directly on [`CreativeSession`](review-context/CreativeSession.cs#L22-L35) under `#if UNITY_EDITOR`, eliminating the redundant `CreativeCaptureSignal` class.

---

### Finding 5: Brittle Scene Hierarchy Naming in Edit Mode Tool
* **Severity:** Low
* **Affected Components:** Authoring tool mapping table (`UI_Level/level_txt_bg (1)`)
* **Concrete Failure Scenario:** The path `UI_Level/level_txt_bg (1)` relies on Unity's default duplicate naming suffix `(1)`. If an artist or UI engineer cleans up the hierarchy by renaming the badge to `DifficultyBadge` or reorganizing `UI_Level`, the authoring tool will report a missing path and fail to bind the group.
* **Recommended Change:** Rename the GameObject in Scene1 to an explicit identifier (e.g., `DifficultyBadge`) before running the authoring tool, and reflect that semantic path in the authoring tool's mapping table.

---

## Targeted Review Questions

### 1. Additive `TimerStarted` Event vs. Alternatives
* **Assessment:** The proposed additive `public event Action TimerStarted;` on [`PrototypeBoard`](review-context/PrototypeBoard.excerpt.cs#L10-L18) is the correct, lowest-risk solution.
* **Rationale:**
  - In [`PrototypeBoard.StartTimer()`](review-context/PrototypeBoard.excerpt.cs#L75-L87), `TryTransitionGameState(GameState.Playing)` invokes `GameStateChanged` *before* `IsTimerStarted = true;` is assigned. Any listener checking `IsTimerActive` during `GameStateChanged` observes `false`.
  - Reordering `IsTimerStarted` before `TryTransitionGameState` carries subtle risk if `TryTransitionGameState` fails or if existing normal-play listeners expect the timer flag to be unset during the transition.
  - Adding `TimerStarted` right after `IsTimerStarted = true; bombElapsedSeconds = 0f;` provides an unambiguous, authoritative event. Because `StartTimer()` guards with `if (IsTimerStarted) return;`, it is guaranteed to fire at most once per board run.

### 2. Editor-Only Static Bridge
* **Assessment:** Acceptable by necessity, but requires lifecycle hygiene.
* **Rationale:** Runtime code in `Assembly-CSharp` cannot reference editor types in `Assembly-CSharp-Editor`. A static C# event guarded by `#if UNITY_EDITOR` is the standard Unity idiom for runtime-to-editor communication in Play Mode. However, to comply with `AGENTS.md` ("Avoid static mutable state..."), the event must live directly on `CreativeSession` (no wrapper class) and must be explicitly unsubscribed/cleared on domain reset or Play Mode exit.

### 3. Hiding UI by `SetActive` vs. Side Effects on Scripts
* **Assessment:** High risk if applied blindly to composite UI panels.
* **Rationale:** As detailed in Finding 2, disabling GameObjects kills DOTween tweens linked with `KillOnDisable` and terminates running Coroutines. The authoring tool and `CreativeSceneBindings` must favor `Canvas.enabled = false` for panels and button groups, reserving `SetActive(false)` only for inert graphical objects like badges or decorative images.

### 4. Timing of Recorder Prepare/Start Mid-Play Mode
* **Assessment:** Technically sound and improves recorder reliability.
* **Rationale:**
  - Calling `PrepareRecording()` and `StartRecording()` after `Scene1` is loaded prevents Unity Recorder's internal helper GameObjects from being instantiated in the splash scene and destroyed during asynchronous scene transition.
  - Unity Recorder supports starting in manual mode at any frame during Play Mode.
  - Because `recording_start_unity_frame` is sampled via `Time.frameCount` inside `StartRecording()`, the pipeline's frame alignment calculation:
    $$\text{video\_origin} = \text{recording\_stop\_unity\_frame} - \text{video\_frame\_count}$$
    remains completely accurate, and the consistency check ($\text{video\_origin} - \text{recording\_start\_unity\_frame} \le 10$) is preserved.

---

## Final Verdict

### **APPROVE WITH CHANGES**

The proposal is approved for implementation subject to incorporating the following required modifications:
1. **Resolve Win Panel Desynchronization:** Pass the evaluated `displayedLevelNumber` to `PrototypeBoard` so that `completedLevelText` on the win panel matches the HUD level label during take tail recording.
2. **Eliminate Redundant Wrapper Classes:** Remove `CreativeTimerFreeze` and `CreativeCaptureSignal`. Place the timer freeze logic directly in [`CreativeSession`](review-context/CreativeSession.cs#L22-L70), and expose `public static event Action<string> CaptureReady;` directly on `CreativeSession` under `#if UNITY_EDITOR`.
3. **Guard Static Event Lifecycle in `CreativeRecorderDriver`:** Ensure one-shot unsubscription from `CaptureReady` upon invocation, and guarantee cleanup in `OnPlayModeStateChanged(ExitingPlayMode)`.
4. **Prioritize Canvas Disabling over Deactivation:** Use `Canvas.enabled = false` (or `CanvasGroup`) in `CreativeSceneBindings` for interactive panels and groups to avoid terminating Coroutines and killing linked DOTween tweens.
