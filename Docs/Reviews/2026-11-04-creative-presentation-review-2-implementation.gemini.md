# Independent Technical Review: Creative Mode Presentation Fixes (Implementation)

**Repository:** Gravity BlockPuzzle (`marketing` branch)  
**Governing Contract:** `AGENTS.md` (Gravity BlockPuzzle Engineering Contract)  
**Artifacts Reviewed:** `changes.diff`, `CreativeSession.cs`, `CreativeRecorderDriver.cs`, `CreativeSceneBindingsAuthoring.cs`, `CreativeTimerFreezePlayModeTests.cs`, `CreativeUI_GameplayClean.asset`, `DECISIONS.md`, `PROPOSAL.md`  
**Review Type:** Implementation Advisory Review (Read-Only)

---

## 1. Compliance with Approved Proposal and Decisions (`DECISIONS.md`)

The implementation accurately reflects the approved design and successfully incorporates all four review resolutions established in [`DECISIONS.md`](review-context/DECISIONS.md):

1. **Win Panel & Level Numbering Consistency (Decision 1):**
   - Implemented via [`GravityLevelRuntime.ConfigureDisplayLevelNumber(int)`](review-context/GravityLevelRuntime.cs#L182-L194) and [`DisplayLevelNumber`](review-context/GravityLevelRuntime.cs#L257-L263), falling back to `CurrentLevelNumber` when unconfigured or `0`.
   - Both the composition root ([`RuntimePieceFactoryBootstrap.UpdateLevelDisplays`](review-context/RuntimePieceFactoryBootstrap.cs#L282-L300)) and [`PrototypeBoard`](review-context/PrototypeBootstrap.cs#L2926-L2942) (`completedLevelText` and `nextLevelButtonLabel`) query `DisplayLevelNumber`.
   - Booster gating, save data, and coin amounts remain strictly bound to authoritative `CurrentLevelNumber`.
2. **`SetActive` Side-Effect Mitigation (Decision 2):**
   - [`CreativeSceneBindings.UIGroupBinding`](review-context/CreativeSceneBindings.cs#L21-L28) now declares `public Graphic[] graphics`.
   - [`CreativeSceneBindingsAuthoring.Author()`](review-context/CreativeSceneBindingsAuthoring.cs#L90-L105) populates `graphics` via `GetComponentsInChildren<Graphic>(true)` and zeroes out `canvases` and `objects`.
   - [`CreativeSession.ApplyUIProfile()`](review-context/CreativeSession.cs#L175-L208) disables `Graphic.enabled = false` instead of deactivating GameObjects. This preserves active `MonoBehaviour` scripts, running Coroutines, and tweens with `KillOnDisable`, while allowing raycasts to pass through.
3. **Static Event Lifecycle & Fail-Safe Recording (Decision 3):**
   - [`CreativeRecorderDriver`](review-context/CreativeRecorderDriver.cs#L51-L85) subscribes to `CreativeSession.CaptureReady` only on `EnteredPlayMode` when `pending != null`.
   - The handler [`OnCaptureReady`](review-context/CreativeRecorderDriver.cs#L68-L84) performs immediate one-shot unsubscription, verifies `request.takeName == takeName`, and consumes `pending`.
   - `ExitingPlayMode` guarantees unsubscription, logs an error if `pending != null` without signalling, and suppresses `<take>.take.json` generation.
4. **Elimination of Unnecessary Wrapper Classes (Decision 4):**
   - Neither `CreativeTimerFreeze` nor `CreativeCaptureSignal` was introduced.
   - Timer freeze logic is consolidated directly inside [`CreativeSession.HoldFrozenTimer()`](review-context/CreativeSession.cs#L227-L232), reacting to both [`board.TimerStarted`](review-context/CreativeSession.cs#L222-L225) and `GameStateChanged(Playing)` (to support revive continues).
   - `CaptureReady` is declared as a static event directly on [`CreativeSession`](review-context/CreativeSession.cs#L22-L29) guarded by `#if UNITY_EDITOR`.
5. **Declined Renaming of Scene Object (Decision 5):**
   - Respected. The authoring tool maps `"UI_Level/level_txt_bg (1)"` directly without modifying production scene hierarchy names.

---

## 2. Impact on Normal (Non-Creative) Gameplay Behaviour

**Normal gameplay behaviour is completely unchanged.**

- **Zero Runtime Divergence Without Request:** In standard Play Mode or production builds, `CreativeTakeRequest.PeekPendingData()` is null. [`CreativeRecorderDriver`](review-context/CreativeRecorderDriver.cs) remains inert. In [`RuntimePieceFactoryBootstrap`](review-context/RuntimePieceFactoryBootstrap.cs#L133-L138), `creativeRequest` is null, so `ConfigureDisplayLevelNumber` is never called.
- **Authoritative Gating Intact:** `displayLevelNumberOverride` defaults to `0` and is reset on `GravityLevelRuntime.ResetLevelSequence()`. When `0`, [`DisplayLevelNumber`](review-context/GravityLevelRuntime.cs#L257-L263) returns `CurrentLevelNumber`. Every label text in normal play evaluates identically to previous behaviour.
- **Additive `TimerStarted` Event:** [`PrototypeBoard.TimerStarted`](review-context/PrototypeBootstrap.cs#L712-L714) is purely observational. In normal play, it has zero listeners. Raising it via `TimerStarted?.Invoke()` inside [`StartTimer()`](review-context/PrototypeBootstrap.cs#L2572-L2579) incurs only a null-check check with zero allocations and no state mutations.

---

## 3. Detailed Technical Findings

### Finding 1: SerializedProperty Enum Value Assignment in Authoring Tool
* **Severity:** Low
* **Affected Component:** [`CreativeSceneBindingsAuthoring.Author()`](review-context/CreativeSceneBindingsAuthoring.cs#L122)
* **Concrete Failure Scenario:**
  Line 122 assigns:
  ```csharp
  element.FindPropertyRelative("group").enumValueIndex = (int)resolved[i].Key;
  ```
  In Unity's `SerializedProperty` API, `enumValueIndex` expects the **0-based index of the enum value within its declaration order**, whereas `intValue` represents the underlying enum integer value. Currently, `CreativeUIGroup` values (0 through 11) are contiguous and match their declaration order, so `(int)resolved[i].Key` coincides with `enumValueIndex`. However, if an engineer adds a new enum member in the middle of `CreativeUIGroup`, reorders them, or introduces explicit non-contiguous values, this assignment will silently serialize the incorrect enum member or throw an `IndexOutOfRangeException` when running the tool.
* **Recommended Change:**
  Assign directly to `intValue` instead:
  ```csharp
  element.FindPropertyRelative("group").intValue = (int)resolved[i].Key;
  ```

---

### Finding 2: Unmanaged Delay Coroutine on Early Session Termination
* **Severity:** Low
* **Affected Component:** [`CreativeSession.SignalCaptureReadyAfterDelay()`](review-context/CreativeSession.cs#L250-L264), [`CreativeSession.End()`](review-context/CreativeSession.cs#L118-L156)
* **Concrete Failure Scenario:**
  In [`Begin()`](review-context/CreativeSession.cs#L94), `coroutineHost.StartCoroutine(SignalCaptureReadyAfterDelay())` is spawned without caching the `Coroutine` handle. If `End()` is invoked before `captureStartDelayFrames` elapse (e.g. an early failure, board exception, or manual test exit during the first 3 frames), the coroutine continues to yield in the background until the frame count completes. Line 256 guards with `if (ended) yield break;` before logging or raising the event, preventing event leakage, but leaving an unmanaged coroutine running on `coroutineHost` across teardown violates clean coroutine ownership principles.
* **Recommended Change:**
  Store the returned `Coroutine` handle in a field (e.g. `captureDelayRoutine`) and stop it explicitly in [`End()`](review-context/CreativeSession.cs#L143-L145), matching the lifecycle pattern already used for `safetyRoutine`.

---

### Finding 3: `CaptureReady` Event Left Static Across Domain Reload Configurations
* **Severity:** Low
* **Affected Component:** [`CreativeSession.CaptureReady`](review-context/CreativeSession.cs#L27)
* **Concrete Failure Scenario:**
  If an exception or early Play Mode exit occurs after subscription in `EnteredPlayMode` but before `ExitingPlayMode` is processed, or if third-party editor code subscribes to `CaptureReady`, the static delegate backing field could retain stale references in an environment where Domain Reload on Enter Play Mode is disabled.
* **Recommended Change:**
  In [`CreativeSession`](review-context/CreativeSession.cs), add an `#if UNITY_EDITOR` reset method (or `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`) to set `CaptureReady = null;`, guaranteeing a pristine static state regardless of domain reload settings.

---

## 4. Evaluation of the New PlayMode Tests

[`CreativeTimerFreezePlayModeTests.cs`](review-context/CreativeTimerFreezePlayModeTests.cs) provides exemplary test coverage and adheres to the project's reflection-based test harness:

1. **`TimerStarted` Contract Verification:**
   - [`TimerStarted_IsRaisedOnceAfterTheTimerIsMarkedStarted`](review-context/CreativeTimerFreezePlayModeTests.cs#L55-L71) validates that calling `StartTimer()` twice raises `TimerStarted` exactly once, and asserts `IsTimerStarted == true` at the moment the event handler executes.
2. **Regression Guard for Normal Play:**
   - [`NormalPlay_TimerCountsDown`](review-context/CreativeTimerFreezePlayModeTests.cs#L73-L80) validates that normal board countdown behaviour is completely uninhibited when no creative session exists.
3. **Synchronous Freeze & Reversible Lifecycle:**
   - [`FrozenCreativeTake_HoldsTheTimerFromItsFirstFrame_AndReleasesItOnEnd`](review-context/CreativeTimerFreezePlayModeTests.cs#L82-L101) tests that the pause is applied synchronously inside `StartTimer()`, verifies zero time is lost after running 10 frames, calls `End()`, and confirms that timer countdown resumes normally.
4. **Negative Mode Verification:**
   - [`NormalTimerModeCreativeTake_DoesNotPauseTheTimer`](review-context/CreativeTimerFreezePlayModeTests.cs#L103-L115) ensures that non-frozen creative takes do not pause the timer.
5. **Test Hygiene:**
   - `TearDown` properly cleans up instantiated GameObjects, `ScriptableObject` instances, and temporary file directories.

---

## 5. AGENTS.md Contract Compliance Analysis

- **Runtime Restrictions:** Compliant. No runtime `Instantiate` / `Destroy` is used (`CreateInstance` and `DestroyImmediate` are restricted to Editor Recorder setup/teardown). No runtime `GameObject.Find`, `GetComponent` polling, or uncached queries are introduced. Normal placement and matrix rules remain untouched.
- **Architecture & Modularity:** Compliant. Dependencies are passed explicitly from the composition root ([`RuntimePieceFactoryBootstrap`](review-context/RuntimePieceFactoryBootstrap.cs#L131-L138)). Single responsibility is preserved without introducing superfluous abstractions.
- **Tween & Presentation Rules:** Compliant. By disabling `Graphic.enabled` rather than calling `SetActive(false)`, linked tweens on UI hierarchies remain active, preventing unintended `KillOnDisable` executions.
- **Performance:** Compliant. Zero LINQ in hot paths; [`ApplyUIProfile()`](review-context/CreativeSession.cs#L175-L208) only iterates small pre-allocated arrays on state transitions and executes with zero per-frame allocations.

---

## Final Verdict

### **APPROVE WITH CHANGES**

The implementation is robust, clean, and fulfills all requirements from `DECISIONS.md`. Normal gameplay is strictly preserved, and the PlayMode tests are thorough. 

Adoption of the following minor changes is recommended prior to merging:
1. In [`CreativeSceneBindingsAuthoring.cs` (line 122)](review-context/CreativeSceneBindingsAuthoring.cs#L122), change `.enumValueIndex` to `.intValue` to prevent future enum reordering or non-contiguous serialization bugs.
2. In [`CreativeSession.cs`](review-context/CreativeSession.cs), track the `Coroutine` returned by `StartCoroutine(SignalCaptureReadyAfterDelay())` and explicitly stop it inside `End()`.
