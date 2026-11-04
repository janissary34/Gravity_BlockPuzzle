# Independent Technical Review: Creative Production Mode (Scripted Pointer & Release Events)

**Reviewer:** Senior Unity / C# Systems & Architecture Reviewer  
**Context:** Independent Second Opinion per `AGENTS.md`  
**Target:** [`PuzzleDragController.cs`](review-context/PuzzleDragController.cs), `IBoardPointerSource.cs`, and `CreativePointerScript.cs`  
**Verdict:** **APPROVE WITH CHANGES**

---

### Executive Summary

The core architectural direction of the proposal is **sound and strongly compliant with the repository's core engineering principles**:
1. **Reuses the authoritative input pipeline:** Routing scripted input through `TrySelectPiece`, `UpdateDragTarget`, `TryMoveSelectedPieceOnGrid`, and `ReleasePiece` completely avoids creating a parallel input authority or bypassing grid validation rules.
2. **Zero normal-play impact:** In normal play (`scriptedPointerSource == null`), the diff reduces to a single reference null-check per frame in [`Update()`](review-context/PuzzleDragController.cs#L188-L245), with zero allocations, zero new singletons, and byte-for-byte behavioral equivalence.
3. **No extra `Update` runners:** Designing `IBoardPointerSource.GetSample(frame)` as an allocation-free pure function of frame number respects the `AGENTS.md` mandate to minimise `Update` users.

However, there are **critical edge-case flaws in release event coverage, replay determinism risks, drag boundary shadowing, and pointer deactivation lifecycle** that must be resolved before this code is merged.

---

### Prioritized Findings & Required Changes

#### 1. [CRITICAL] Dropped Release Event on Failed Restore & Combined Snap Failure
* **File:** [`PuzzleDragController.cs`](review-context/PuzzleDragController.cs#L1420-L1435) (lines 1420–1435 in proposed diff)
* **Problem:** In [`ReleasePiece()`](review-context/PuzzleDragController.cs#L1370-L1445), `PieceReleased?.Invoke(..., false)` is raised only inside `if (hasSelectedPieceStartAnchor && TryRestoreGridRelease(...))`. If `TryRestoreGridRelease` fails, the method logs a `[GridRelease] Failed to restore...` warning and clears `selectedPiece = null` **without invoking `PieceReleased`**. Similarly, in the combined group release branch (`if (isCombined)`), if `TryCommitSafeSnapRelease` returns `false`, `PieceReleased` is never raised.
* **Impact:** Any downstream consumer (e.g. [`CreativeSession`](review-context/CreativeSession.cs#L20-L45) writing `events.json`) that observed a drag start will see a permanently unclosed drag lifecycle, corrupting marketing edit point timestamps.
* **Required Change:** Ensure every exit from [`ReleasePiece()`](review-context/PuzzleDragController.cs#L1370-L1445) fires `PieceReleased`:
  ```csharp
  // Capture piece ID upfront before any branch nulls selectedPiece
  int releasingPieceId = selectedPiece.SourcePieceId;
  ...
  // In the failed restore fallback path:
  Debug.LogWarning($"[GridRelease] Failed to restore '{selectedPiece.name}'...", selectedPiece);
  PieceReleased?.Invoke(releasingPieceId, false);
  ```

#### 2. [CRITICAL] Boundary Shadowing and Silent Drag Loss in `CreativePointerScript`
* **File:** `CreativePointerScript.proposed.cs` (`GetSample` loop)
* **Problem 1 (Boundary Shadowing):** In `GetSample`:
  ```csharp
  int end = d.StartFrame + Math.Max(1, d.MoveFrames);
  if (t < d.StartFrame || t > end) continue;
  ```
  If Drag 0 ends at frame 10 (`end = 10`) and Drag 1 begins at frame 10 (`StartFrame = 10`):
  On frame `t = 10`, the loop matches Drag 0 (`t == end`) and returns `Released = true`. Drag 1 is never evaluated on frame 10.
  On frame `t = 11`, Drag 0 is skipped (`11 > 10`), and Drag 1 matches. But `Pressed = (t == d.StartFrame)` evaluates to `(11 == 10)` which is **false**!
  Because `Pressed` is false on frame 11, [`TrySelectPiece`](review-context/PuzzleDragController.cs#L1230-L1285) is never called. **Drag 1 is completely dropped and never executes.**
* **Problem 2 (Single-Frame Selection Fragility):** Because `Pressed` is true for only a single frame, if the board is temporarily busy settling a cascade or fall tween (`CanBeginPieceInteraction == false`), [`TrySelectPiece`](review-context/PuzzleDragController.cs#L1230-L1285) fails on `StartFrame`. In subsequent frames, `Held` is true but `selectedPiece` is null, so the drag silently fails for its entire duration.
* **Required Change:**
  1. Enforce that consecutive drags have at least a 1-frame gap (`d[i+1].StartFrame > d[i].StartFrame + d[i].MoveFrames`), or validate this during `CreativePointerScript` initialization.
  2. If `selectedPiece == null` and `sample.Held` is true while a drag is active, log an explicit warning in Editor/Creative mode indicating that a scripted drag failed to acquire its piece.

#### 3. [HIGH] Indefinite Input Lockout via `CreativePointerScript.IsActive`
* **File:** `CreativePointerScript.proposed.cs`
* **Problem:** `public bool IsActive => originFrame >= 0;` remains `true` indefinitely after `Start()` is called, even when all drags have completed (`t > max(end)`).
* **Impact:** In [`PuzzleDragController.Update()`](review-context/PuzzleDragController.cs#L240-L250), `scriptedPointerSource.IsActive` will continuously evaluate to `true`, permanently starving `ProcessMouseInput()` and `ProcessTouchInput()`. During tail frames, post-take inspection, or interactive editor testing, device input is dead unless [`ConfigureScriptedPointerSource(null)`](review-context/PuzzleDragController.cs#L75-L85) is manually called.
* **Required Change:** Precompute the maximum end frame `totalFrames` in `CreativePointerScript`. Define `IsActive` as:
  ```csharp
  public bool IsActive => originFrame >= 0 && (Time.frameCount - originFrame) <= totalFrames;
  ```
  Once the script timeline has concluded, `IsActive` drops to `false`, naturally restoring normal device input.

#### 4. [HIGH] Fragile and Redundant Coordinate Conversions (World $\to$ Screen $\to$ World)
* **File:** `CreativePointerScript.proposed.cs` and [`PuzzleDragController.cs`](review-context/PuzzleDragController.cs#L1200-L1215)
* **Problem:** In `CreativePointerScript.Start()`, `FromWorld` and `ToWorld` are converted to screen pixels using `camera.WorldToScreenPoint(...)`. Then in `ProcessScriptedInput()`, `sample.ScreenPosition` is converted back to world coordinates using [`PointerWorldPosition(...)`](review-context/PuzzleDragController.cs#L1495-L1505).
* **Impact:**
  - If Unity Recorder adjusts the Game View resolution or aspect ratio after `CreativePointerScript.Start()` is invoked, the precomputed screen positions become distorted or offset.
  - Floating-point round-tripping across camera matrices can introduce boundary jitter in [`WorldToFineCell(...)`](review-context/PuzzleDragController.cs#L1290-L1305), causing hit-test misses on pieces near cell edges.
* **Required Change:**
  Add a `WorldPosition` field to `BoardPointerSample` (or pass it directly). If `ScreenPosition` is retained on `BoardPointerSample` to maintain semantic parity with device pointers, allow `sample.WorldPosition` to take precedence if set, eliminating the redundant matrix round-trip.

#### 5. [MEDIUM] Clean Up Injected Pointer Source and Subscriptions on Disable
* **File:** [`PuzzleDragController.cs`](review-context/PuzzleDragController.cs#L175-L185) (`OnDisable`)
* **Problem:** `scriptedPointerSource` is not reset in `OnDisable()`. If a scene reloads or restarts Play Mode without destroying the controller instance, a stale pointer source could persist. Furthermore, `PieceReleased` delegate subscriptions are not explicitly decoupled on tear-down.
* **Required Change:** In [`OnDisable()`](review-context/PuzzleDragController.cs#L175-L185):
  ```csharp
  scriptedPointerSource = null;
  ```
  Ensure [`CreativeSession.End()`](review-context/CreativeSession.cs#L50-L75) unbinds its listener from `PieceReleased` and calls `ConfigureScriptedPointerSource(null)`.

---

### In-Depth Architectural Evaluation

#### 1. Mouse-Semantics Mirror & Frame Sequencing
The frame sequencing in `ProcessScriptedInput` mirrors `ProcessMouseInput` accurately:
- **Press frame:** `sample.Pressed == true` triggers [`TrySelectPiece(pointer)`](review-context/PuzzleDragController.cs#L1230-L1285), caching `grabOffset = body.position - pointer`. In that same frame, `sample.Held == true` calls [`UpdateDragTarget`](review-context/PuzzleDragController.cs#L1140-L1155) with `pointer + grabOffset == body.position`, resulting in `pendingDragIntent == 0`. This prevents artificial single-frame jump impulses upon selection.
- **Hold frames:** `sample.Held == true` updates `pendingDragIntent`, which is consumed at the bottom of [`Update()`](review-context/PuzzleDragController.cs#L240-L255) by [`TryMoveSelectedPieceOnGrid`](review-context/PuzzleDragController.cs#L960-L1015).
- **Release frame:** `sample.Released == true` updates the target position to the final point, then calls [`ReleasePiece()`](review-context/PuzzleDragController.cs#L1370-L1445). Inside `ReleasePiece()`, `TryMoveSelectedPieceOnGrid(selectedPiece)` consumes the final intent, followed immediately by safe snap commit and release presentation. `selectedPiece` is nulled, preventing post-release movement.

#### 2. Interaction with Existing Guards
The placement in [`Update()`](review-context/PuzzleDragController.cs#L188-L245) is after:
- `inputLockedByGameState`
- `BoosterFirstUseTutorialRuntime.Gate.BlocksBoardDrag`
- Shredder lock check
- `HammerBooster.IsTargeting || RocketBooster.IsTargeting`
- `LevelTimerUI.IsGameOver`
- `CapturePiecesAtShredderBoundary()`

Because scripted input is placed directly where `ProcessMouseInput()` previously ran, all guard conditions abort or release the drag identically to standard gameplay.

#### 3. Determinism & Pacing Risks
- **FixedUpdate vs Update Aliasing:** [`AdvanceGridGravityPresentation()`](review-context/PuzzleDragController.cs#L360-L395) runs in `FixedUpdate()`, whereas input runs in `Update()`. Under Unity Recorder constant frame rate (e.g. 30 fps), if `Time.fixedDeltaTime = 0.02s` (50 Hz), there are alternating frames with 1 vs 2 fixed updates. If fall tweens are evaluated against discrete visual thresholds, cascade completion times can jitter by $\pm 1$ frame.
- **Mitigation:** Ensure automated creative takes configure adequate spacing frames between the release of one piece and the selection of the next, allowing the grid cascade to settle fully before the next drag starts.

#### 4. Performance & Allocations
- `BoardPointerSample` is a pure struct (16 bytes).
- `CreativePointerScript.GetSample()` allocates zero heap memory per frame (indexed loop over a preallocated `List<Drag>`).
- Normal play executes a single pointer null-check.
- **Contract Compliance:** Fully adheres to the per-frame allocation, LINQ, and hot-path rules in `AGENTS.md`.

---

### Final Verdict

**APPROVE WITH CHANGES**

The proposed design is architecturally sound and respects the core tenets of `AGENTS.md`. Implementing the 5 prioritized changes above—specifically ensuring release events are guaranteed on all exit branches and eliminating drag boundary shadowing in `CreativePointerScript`—will ensure production-grade reliability for marketing recordings and automated takes.
