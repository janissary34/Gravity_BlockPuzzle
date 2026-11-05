# Independent Senior Unity Technical Review: Implementation Assessment

**Subject:** Creative Mode Framing (Board Viewport Positioning & Progress Bar Placement)  
**Implementation Source:** `actual.diff`, `CreativeFramingProfile.cs`, `CreativeFramingTests.cs`, and associated assets  
**Prior Context:** `review-1-proposal.md`, `DECISIONS-AND-VALIDATION.md`  
**Contract Authority:** `AGENTS.md` (Gravity BlockPuzzle Engineering Contract)  

---

## 1. Executive Summary

This review assesses the implemented changes for reusable creative framing against the approved design proposal, the implementer's recorded decisions, and the mandatory standards of `AGENTS.md`.

The implementation correctly adheres to the primary presentation-decoupling requirements of `AGENTS.md`:
* **Coordinate Invariance:** Board matrix coordinates, authoritative tile occupancy, line clears, and physics handoffs remain origin-centered and completely decoupled from presentation camera translation.
* **Input Invariance:** Manual drag unprojection (`PointerWorldPosition`) and scripted drag playback (`CreativePointerScript`) operate through the live camera after framing translation, preserving exact world-space snapping with sub-micro-unit numerical precision.
* **UI Math & Hierarchy:** Progress bar repositioning uses `RectTransformUtility.ScreenPointToWorldPointInRectangle` on the slider's canvas plane, accurately accounts for arbitrary pivots via `TransformPoint(rect.center)`, maintains X/Z coordinates, and caches position for restoration in `End()`.

However, the implementation fails to resolve the **MAJOR** static lifecycle hazard identified in Review 1 due to an incomplete adoption in `RuntimePieceFactoryBootstrap`. Additionally, declining to enforce a safe lower bound on `boardTopViewportInset` while leaving pool capacity at 242 introduces a critical runtime exception hazard if designers configure low insets.

**Verdict: APPROVE WITH CHANGES**

---

## 2. Evaluation Against Proposal & Review 1 Decisions

| Proposal Requirement | Review 1 Mandate | Implementer Decision (`DECISIONS-AND-VALIDATION.md`) | Implementation Code State | Compliance |
|---|---|---|---|---|
| **Static State Reset** | Unconditional bootstrap reset (`framing != null ? inset : 0f`) | **Partly Declined:** Relied exclusively on `ResetLevelSequence` via `SubsystemRegistration`; declined bootstrap reset. | `RuntimePieceFactoryBootstrap.cs` guards `ConfigureBoardTopViewportInset` behind `if (creativeRequest.Config.Framing != null)`. | **NON-COMPLIANT** (Finding 1) |
| **Pool Capacity Headroom** | Rebuild `LevelRuntimeRoot` to $\ge 280$ or clamp inset range to $[0.12, 0.24]$ | **Declined pool rebuild;** adopted range $[0, 0.40]$ with tooltip. | Kept pool at 242; `CreativeFramingProfile.cs` uses `[Range(0f, .4f)]`. | **PARTIAL / AT RISK** (Finding 2) |
| **Safe UI Math & Teardown** | Screen-to-world unprojection, position caching, null guards | **Adopted.** ScreenPointToWorldPointInRectangle, cached position, null guards. | Implemented in `CreativeSession.cs` (`PlaceProgressBar`, `RestoreProgressBar`). | **COMPLIANT** |
| **Defensive Configuration** | Prevent invalid out-of-range inset and gap values | **Adopted.** Range attributes and tooltips added. | Range attributes added; lower bound lacks safe non-zero clamping. | **PARTIALLY COMPLIANT** |
| **Documentation** | Update `Docs/` per `AGENTS.md` documentation workflow | **Adopted.** Updated `Docs/CreativeMode.md`. | Extensively documented framing, pooling extent, and bindings in `Docs/CreativeMode.md`. | **COMPLIANT** |

---

## 3. Deep-Dive Technical Analysis

### 3.1 Board Authority & Coordinate Systems (INVARIANT)
In `GravityLevelRuntime.ResolveCameraVerticalPosition`:
$$\Delta Y_{\text{cam}} = - (0.24 - \text{topInset}) \times \text{CameraSize} \times 2$$
At $\text{topInset} = 0.16$ and $\text{CameraSize} = 10$, $\Delta Y_{\text{cam}} = -1.6\text{ units}$.
* Authoritative board matrix bounds ($[-C/2, C/2] \times [-R/2, R/2]$), cell occupancy, and gravity step resolution operate strictly in world/matrix space without querying camera transforms.
* `CreateViewportEnvironmentBackdrop` and `ResolveLowerPresentationExtent` compute extents using the configured camera position during level construction. The visual environment scales predictably with the camera.

### 3.2 Input & Drag Snapping (INVARIANT)
* **Manual Input:** `PuzzleDragController.PointerWorldPosition` unprojects screen touch coordinates using `gameCamera.ScreenToWorldPoint`. Because the camera is shifted down by $1.6$ units, a touch on screen translates back to the exact same world coordinate $(X, Y)$ on the board.
* **Scripted Playback:** `CreativePointerScript.Start` converts target world points to screen points using the repositioned camera. In `CreativePointerScript.Update`, screen points are delivered to `PuzzleDragController`, which unprojects them back through the same camera matrix. World-space drift is bounded by float epsilon ($< 10^{-6}$), far below grid snap margins.

### 3.3 Progress Bar Positioning Math
In `CreativeSession.PlaceProgressBar`:
```csharp
Vector3 boardTop = camera.WorldToScreenPoint(new Vector3(0f, config.Level.boardRows * .5f, 0f));
var target = new Vector2(boardTop.x, boardTop.y + framing.ProgressBarBoardGap * camera.pixelHeight);
Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
if (RectTransformUtility.ScreenPointToWorldPointInRectangle(bar, target, uiCamera, out Vector3 targetWorld))
{
    originalProgressBarPosition = bar.position;
    progressBarMoved = true;
    float barCentreY = bar.TransformPoint(bar.rect.center).y;
    bar.position += new Vector3(0f, targetWorld.y - barCentreY, 0f);
}
```
* `bar.TransformPoint(bar.rect.center).y` maps the RectTransform's local center to world space, correctly accounting for local pivot offsets and canvas scale factors.
* Adding `new Vector3(0f, targetWorld.y - barCentreY, 0f)` to `bar.position` applies the vertical delta while preserving authored horizontal centering and Z depth.
* Teardown in `CreativeSession.RestoreProgressBar` checks `progressBarMoved` and `bindings?.ProgressBar != null` before restoring `originalProgressBarPosition`.

---

## 4. Findings & Risk Register

### Finding 1: Static State Leak Across Sequential Takes & Non-Reload Transitions
* **Severity:** **MAJOR**
* **Contract Reference:** `AGENTS.md` § *State and data integrity* ("Avoid multiple systems independently mutating the same authoritative state"), § *Architecture* ("Avoid static mutable state unless the data is truly process-wide...").
* **Description:**  
  In `RuntimePieceFactoryBootstrap.cs`:
  ```csharp
  if (creativeRequest != null)
  {
      GravityLevelRuntime.ConfigureLevelOverride(creativeRequest.Config.Level);
      GravityLevelRuntime.ConfigureDisplayLevelNumber(creativeRequest.Config.DisplayLevelNumber);
      if (creativeRequest.Config.Framing != null)
          GravityLevelRuntime.ConfigureBoardTopViewportInset(creativeRequest.Config.Framing.BoardTopViewportInset);
  }
  ```
  The implementer declined Review 1's required unconditional bootstrap reset, arguing that `ResetLevelSequence` runs via `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`.  
  This rationale is flawed:
  1. `SubsystemRegistration` runs **only** when entering Play Mode. It does **not** run between scenes, during in-editor test runs, or across multiple takes in a single session.
  2. If **Take 1** specifies a framing profile (`BoardTopViewportInset = 0.16f`), and **Take 2** has `framing = null` ("Empty = the game's own framing"), `creativeRequest.Config.Framing != null` evaluates to `false`. `ConfigureBoardTopViewportInset` is never called. Take 2 inadvertently inherits Take 1's $0.16$ inset.
  3. If normal gameplay is started or tested after a creative take without reloading the domain, `creativeRequest` is null. Normal gameplay inherits the creative framing override.
* **Required Change:**  
  In `RuntimePieceFactoryBootstrap.cs`, make the configuration unconditional across all modes:
  ```csharp
  float framingInset = (creativeRequest != null && creativeRequest.Config.Framing != null)
      ? creativeRequest.Config.Framing.BoardTopViewportInset
      : 0f;
  GravityLevelRuntime.ConfigureBoardTopViewportInset(framingInset);
  ```

---

### Finding 2: Unclamped Small Insets Leading to Visual Pool Exhaustion Exception
* **Severity:** **MAJOR**
* **Contract Reference:** `AGENTS.md` § *Pooling rules* ("Pools throw on exhaustion... Prewarm expected gameplay pools...").
* **Description:**  
  The implementer declined to increase `LevelRuntimeRoot` pool capacity from 242 to $\ge 280$.  
  At inset $0.16$ on a standard 9:16 aspect ratio, peak visual tile consumption reaches **234 / 242** (only 8 tiles of headroom).  
  However, `CreativeFramingProfile.cs` defines `[Range(0f, .4f)]` for `boardTopViewportInset`.  
  If a designer sets an inset such as $0.05$ or $0.08$, or runs a take on a taller mobile device format (e.g., 9:19.5 or 9:20), `ResolveLowerPresentationExtent` will demand $> 242$ tiles.  
  Because `GameObjectPool.Spawn` throws upon capacity exhaustion, level generation will fail with an unhandled exception and abort the take.
* **Required Change:**  
  Enforce a defensive safe minimum on non-zero insets inside `GravityLevelRuntime.cs`:
  ```csharp
  public static void ConfigureBoardTopViewportInset(float viewportFraction)
  {
      boardTopViewportInsetOverride = viewportFraction <= 0f 
          ? 0f 
          : Mathf.Clamp(viewportFraction, 0.12f, 0.40f);
  }
  ```
  Alternatively, update `CreativeFramingProfile.cs` to document and enforce `[Range(0.12f, 0.40f)]` with a separate toggle for default framing.

---

### Finding 3: Missing Fallback for `canvas.worldCamera` in ScreenSpaceCamera Mode
* **Severity:** **MINOR**
* **Contract Reference:** `AGENTS.md` § *Code quality* ("Do not silently swallow exceptions or invalid state... Fail safely").
* **Description:**  
  In `CreativeSession.cs` (line 179):
  ```csharp
  Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
  ```
  If `canvas.renderMode == RenderMode.ScreenSpaceCamera` but `canvas.worldCamera` is unassigned (e.g., dynamic UI instantiation or missing inspector reference in a test scene), `uiCamera` evaluates to `null`.  
  Passing `null` as the camera to `ScreenPointToWorldPointInRectangle` for a ScreenSpaceCamera canvas causes Unity to evaluate coordinates against an overlay plane at $Z=0$, resulting in corrupted world Y placement.  
  `CreativeSession` already validates that its injected `camera != null` on line 170.
* **Required Change:**  
  Provide a safe fallback to `camera`:
  ```csharp
  Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
      ? null
      : (canvas.worldCamera != null ? canvas.worldCamera : camera);
  ```

---

### Finding 4: Test Suite Gaps for Progress Bar Lifecycle and Framing Bootstrap
* **Severity:** **MINOR**
* **Contract Reference:** `AGENTS.md` § *Development workflow* ("Every completed development step must leave the project in a compilable state and preserve unrelated gameplay behaviour").
* **Description:**  
  The new tests in `CreativeFramingTests.cs` cover only camera offset arithmetic and reflection-based resets of `GravityLevelRuntime`.  
  The following key paths have no automated test coverage:
  1. `PlaceProgressBar` positioning and pivot offset handling.
  2. `RestoreProgressBar` restoring original `RectTransform.position`.
  3. `RuntimePieceFactoryBootstrap` resetting framing when `creativeRequest == null` or `framing == null`.
  4. Behavioral verification when `ProgressBarBoardGap <= 0f`.
* **Required Change:**  
  Add unit tests in `CreativeFramingTests.cs` covering sequential bootstrap calls and verifying progress bar position caching and restoration.

---

## 5. Required Actions Before Merge

1. **Fix Bootstrap Reset (`RuntimePieceFactoryBootstrap.cs`):**  
   Call `GravityLevelRuntime.ConfigureBoardTopViewportInset(framingInset)` unconditionally, passing `0f` when `creativeRequest == null` or `creativeRequest.Config.Framing == null`.
2. **Add Runtime Inset Clamping (`GravityLevelRuntime.cs`):**  
   Clamp non-zero insets to `Mathf.Clamp(viewportFraction, 0.12f, 0.40f)` to prevent visual pool exhaustion exceptions at runtime.
3. **Add Camera Fallback (`CreativeSession.cs`):**  
   Fallback to the injected `camera` if `canvas.worldCamera` is null in ScreenSpaceCamera mode.
4. **Expand Test Suite (`CreativeFramingTests.cs`):**  
   Add a test validating that subsequent level setups without framing profiles execute with default framing ($0\text{f}$).

---

## 6. Final Verdict

```text
================================================================================
VERDICT: APPROVE WITH CHANGES
The architectural implementation, coordinate-space decoupling, input invariance,
and UI projection math are high quality and adhere to AGENTS.md.
Approval is granted contingent upon resolving the static state reset in
RuntimePieceFactoryBootstrap and applying defensive clamping to prevent pool
exhaustion on small insets.
================================================================================
```
