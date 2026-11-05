# Independent Senior Unity Technical Review

**Proposal:** Reusable Creative Mode Framing (Board Position, Progress Bar) and Minimal UI Profile  
**Context:** Antigravity CLI / Codex Independent Technical Review  
**Contract Authority:** `AGENTS.md` (Gravity BlockPuzzle Engineering Contract)

---

## Executive Summary & Verdict

The proposal presents a well-researched, architecturally sound solution for per-creative framing and UI composition. Intercepting `ResolveCameraVerticalPosition` at level initialization is the correct architectural choice: it preserves the mathematical coupling between camera placement, environment backdrop centering, and `ResolveLowerPresentationExtent`, avoiding the visual seam defects that naive post-build camera translation would cause.

Authoritative gameplay state, matrix grid coordinates, board rule evaluation, and scripted drag playback remain completely untouched.

However, the proposal has two **Major** risks regarding static state lifecycle hygiene and pooled presentation capacity, along with several **Minor** UI math and authoring hazards that must be addressed prior to implementation.

**Final Verdict: APPROVE WITH CHANGES**

---

## Analysis of Normal Play, Drag Coordinates, Scripted Input & Synchronization

### 1. Normal Play Impact: ZERO (with required static reset fix)
* Normal play does not supply a `creativeRequest`. When `creativeRequest == null`, `framing` is unassigned.
* If the static override in `GravityLevelRuntime` is rigorously defaulted and cleared, `topInset` defaults to `configuredBoardPresentationConfig.BoardTopViewportInset` (0.24), leaving camera position, backdrop dimensions, and presentation extents identical to existing production code.

### 2. Drag Coordinates (Manual Input): INVARIANT
* The authoritative board coordinate system is origin-centered at $(0, 0)$ with bounds determined strictly by `level.boardColumns` and `level.boardRows`.
* In `PuzzleDragController.cs`, `PointerWorldPosition` unprojects screen pixels via `gameCamera.ScreenToWorldPoint`.
* Shifting the camera vertically down shifts the rendered board upward in screen space. When a player touches a cell on screen, the camera unprojects that screen point back to the exact same world coordinate. Board occupancy and grid mapping in `NearestCoordinate` remain mathematically identical.

### 3. Scripted Input (`CreativePointerScript`): INVARIANT
* `CreativePointerScript.Start(Camera camera)` converts authored `fromWorld` and `toWorld` vectors to screen coordinates via `camera.WorldToScreenPoint`.
* Because `Start` runs on the live camera *after* the camera has been positioned by `GravityLevelRuntime`, the screen points accurately reflect the new camera position.
* During drag playback, `dragController` converts these screen coordinates back to world space using `camera.ScreenToWorldPoint` on the same camera:
  $$\text{World} \xrightarrow{\text{WorldToScreen}} \text{Screen} \xrightarrow{\text{ScreenToWorld}} \text{World}$$
  At orthographic float precision, world position drift is $< 10^{-6}$ units, orders of magnitude below the fine-grid snapping threshold. Grid snapping remains identical.

### 4. Event Synchronization & Board Authority: INVARIANT
* Per `AGENTS.md`, presentation is strictly decoupled from gameplay authority. Piece placement, gravity settling, line clears, booster usage, and state transitions are coordinate-driven matrix events, never collider/raycast driven.
* Moving the camera vertical offset and repositioning the progress bar does not affect `PrototypeBoard`, `LevelProgressManager`, or state progression.

---

## Findings & Risk Register

### Finding 1: Static State Leak across Sessions / Test Suites
* **Severity:** **MAJOR**
* **Contract Reference:** `AGENTS.md` § *State and data integrity*, § *Architecture* ("Avoid static mutable state unless the data is truly process-wide...").
* **Description:**  
  Proposal item 3 introduces `GravityLevelRuntime.ConfigureBoardTopViewportInset(float)`, reset at line ~67 where `displayLevelNumberOverride` is reset. Proposal item 4 calls this in `RuntimePieceFactoryBootstrap` *only* inside `if (creativeRequest != null)`.  
  If a Creative take runs and sets `boardTopViewportInsetOverride = 0.16f`, and subsequent play mode runs (or integration/unit test runs with domain reload disabled) load a standard level without a creative request, `creativeRequest` is null. If `GravityLevelRuntime` does not execute line ~67 prior to level configuration, the override remains active. Standard gameplay or subsequent unit tests will inadvertently inherit the creative framing override.
* **Required Change:**  
  1. In `RuntimePieceFactoryBootstrap`, do not guard the call exclusively behind `if (creativeRequest != null)`. Instead, explicitly pass the value or reset to 0:
     ```csharp
     float framingInset = creativeRequest?.Config?.Framing != null 
         ? creativeRequest.Config.Framing.BoardTopViewportInset 
         : 0f;
     GravityLevelRuntime.ConfigureBoardTopViewportInset(framingInset);
     ```
  2. Ensure `GravityLevelRuntime` resets `boardTopViewportInsetOverride = 0f` in its static teardown/reset path (line ~67).
  3. Include a unit test validating that entering normal level setup leaves the inset at default.

---

### Finding 2: Lower Presentation Extent & Pool Exhaustion Risk
* **Severity:** **MAJOR**
* **Contract Reference:** `AGENTS.md` § *Pooling rules* ("Pools throw on exhaustion... Prewarm expected gameplay pools...").
* **Description:**  
  `ResolveLowerPresentationExtent` extends lower frame side walls and grid tiles to reach the bottom of the viewport:
  $$\text{extent} = \max(0, \text{cameraSize} - \text{halfHeight} - \text{cameraVerticalPosition} + \text{ViewportBottomPadding})$$
  Because reducing `boardTopViewportInset` from `0.24` to `0.16` lowers `cameraVerticalPosition`, the extent increases, requiring additional visual and frame tiles.  
  The spike log demonstrates that peak visual pool demand increases from `214/242` to `234/242` at inset `0.16`. This leaves **only 8 tiles of headroom** before pool exhaustion.  
  If a take is configured for a taller aspect ratio (e.g., 9:19.5 or 9:20 modern mobile formats rather than 9:16) or an inset below `0.16` is used, visual pool capacity (242) will be exceeded at runtime, causing `GameObjectPool.Spawn` to throw and aborting the take.
* **Required Change:**  
  1. Authoring root pool capacity must not operate with an 8-tile margin. Run `Gravity Puzzle/Level Authoring/Rebuild Level Runtime Root` to increase visual pool capacity to a safe threshold (e.g., 280–300) before merging this feature.
  2. In `CreativeFramingProfile`, enforce `[Range(0.12f, 0.24f)]` on `boardTopViewportInset` to prevent designers from setting arbitrarily small insets that trigger capacity exceptions.

---

### Finding 3: `RectTransform` Coordinate Space & Anchoring Assumptions
* **Severity:** **MINOR**
* **Contract Reference:** `AGENTS.md` § *Engineering principles* ("Predictable state changes over hidden side effects").
* **Description:**  
  In proposal item 6, the bar's position is calculated using `camera.WorldToScreenPoint` and `RectTransformUtility`.  
  Because `InGameUI` is `Screen Space - Camera`, setting `progressBar.position` directly vs. `anchoredPosition` has subtle edge cases:
  - If `UI_Progress_Slider` has non-centered anchors (e.g., top-stretch or top-center), modifying `anchoredPosition` requires translating screen point to parent local space via `RectTransformUtility.ScreenPointToLocalPointInRectangle`.
  - If `progressBar` is wrapped in a `LayoutGroup` or has a `ContentSizeFitter`, direct transform modifications are overwritten on the subsequent layout pass.
* **Required Change:**  
  1. Verify in `Scene1` that `UI_Progress_Slider` is not parented under an active `LayoutGroup`.
  2. Use `RectTransformUtility.ScreenPointToWorldPointInRectangle` on the canvas plane and assign `progressBar.position = new Vector3(progressBar.position.x, targetWorldPoint.y, progressBar.position.z)`, while caching `Vector3 originalPosition = progressBar.position` for exact restoration in `End()`.
  3. Guard restoration in `CreativeSession.End()` against null or destroyed objects:
     ```csharp
     if (barPositionModified && bindings != null && bindings.ProgressBar != null)
     {
         bindings.ProgressBar.position = originalProgressBarPosition;
         barPositionModified = false;
     }
     ```

---

### Finding 4: Visual Coupling Between `Timer` Visibility and `ProgressBarBoardGap`
* **Severity:** **MINOR**
* **Contract Reference:** `AGENTS.md` § *Required data and presentation* ("Presentation must react to lifecycle state... explicit data flow").
* **Description:**  
  `Time` (the level timer) is a child of `UI_Progress_Slider`. At `gap = 0.03` (~58 pixels at 1080x1920), if a take uses a UI profile where `Timer` is visible, the timer overlaps the top row of the board.  
  The proposal relies on tribal knowledge ("A timer-focused creative uses a UI profile with Timer visible and a larger gap"). If a user pairs `CreativeFraming_BoardCentered` (`gap = 0.03`) with a UI profile showing `Timer`, the resulting video will have corrupted framing.
* **Required Change:**  
  1. Document this hierarchy coupling clearly in the tooltip of `progressBarBoardGap` on `CreativeFramingProfile`.
  2. Consider adding an authoring validator or editor warning in `CreativeTakeConfigCustomEditor` if `uiProfile` has `Timer` visible and `framing.progressBarBoardGap < 0.07f`.

---

### Finding 5: Documentation Workflow Compliance
* **Severity:** **MINOR**
* **Contract Reference:** `AGENTS.md` § *Documentation workflow* ("Update the relevant document in the same change when implementation alters system ownership, lifecycle, state flow, pooling, configuration ownership, public contracts, asset-authoring workflow...").
* **Description:**  
  The proposal introduces a new asset authoring requirement (rerunning the scene binding tool to bind `UI_Progress_Slider`), adds public configuration APIs to `GravityLevelRuntime`, and establishes `CreativeFramingProfile`. The proposal's validation plan does not specify updating documentation.
* **Required Change:**  
  Update `Docs/CreativeMode.md` (or relevant presentation documentation) in the same changeset, documenting the framing profile workflow, scene binding authoring step, and pool capacity limits.

---

### Finding 6: Configuration Defensive Sanitization
* **Severity:** **ADVISORY**
* **Contract Reference:** `AGENTS.md` § *Configuration rules*.
* **Description:**  
  `CreativeFramingProfile` fields (`boardTopViewportInset` and `progressBarBoardGap`) currently treat `0` as "use game default". Negative values or values $> 0.5$ are invalid.
* **Required Change:**  
  Add validation attributes to `CreativeFramingProfile.cs`:
  ```csharp
  [Tooltip("Board top inset as fraction of viewport height. 0 = use game default (0.24).")]
  [Range(0f, 0.40f)]
  [SerializeField] private float boardTopViewportInset;

  [Tooltip("Gap from board top to progress bar center as fraction of viewport height. 0 = leave in authored position.")]
  [Range(0f, 0.20f)]
  [SerializeField] private float progressBarBoardGap;
  ```

---

## Evaluation of Simpler Alternatives Considered

1. **Post-Build Camera Offset (Rejected in Proposal):**
   * *Reviewer Assessment:* **Strongly Agree with Rejection.** Moving the camera after build breaks the contract between `ResolveLowerPresentationExtent` and `CreateViewportEnvironmentBackdrop`, exposing the edge of the world backdrop and requiring fragile coroutines/cleanups.
2. **Duplicating `BoardPresentationConfig` per Take (Rejected in Proposal):**
   * *Reviewer Assessment:* **Strongly Agree with Rejection.** Creates redundant SO assets with large amounts of duplicate presentation tuning data, violating § *Configuration rules*.
3. **Passing `CreativeFramingProfile` Directly into `GravityLevelRuntime`:**
   * *Reviewer Assessment:* **Do Not Do This.** The proposal's decision to pass a primitive `float` (`ConfigureBoardTopViewportInset(float)`) is superior because it prevents `GravityLevelRuntime` from taking a compilation dependency on `GravityPuzzle.Creative.Config`.

---

## Required Adjustments Before Implementation

1. **Unconditional Bootstrap Configuration:** Update `RuntimePieceFactoryBootstrap` to explicitly configure `ConfigureBoardTopViewportInset(framing != null ? framing.BoardTopViewportInset : 0f)` so `0f` is enforced whenever creative framing is absent.
2. **Prewarm Pool Capacity:** Rebuild `LevelRuntimeRoot` so visual capacity is increased from 242 to at least 280, providing adequate headroom for small insets and varied capture aspect ratios.
3. **Safe UI Math & Null-Guarded Teardown:** Ensure `CreativeSession` uses world-point unprojection for `progressBar` positioning, caches `originalPosition`, and checks `bindings?.ProgressBar != null` prior to position restoration in `End()`.
4. **Range Attributes:** Decorate `CreativeFramingProfile` fields with `[Range(...)]` attributes to prevent out-of-bounds configuration.
5. **Documentation:** Update the relevant documents under `Docs/` as required by `AGENTS.md`.

---

## Verdict

```text
================================================================================
VERDICT: APPROVE WITH CHANGES
The proposed design is architecturally sound and respects board authority,
coordinate systems, and drag input invariants. Implementation may proceed
once the static reset lifecycle, pool capacity headroom, and UI coordinate
safeguards detailed above are incorporated.
================================================================================
```
