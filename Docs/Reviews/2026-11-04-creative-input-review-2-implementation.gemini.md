# Independent Technical Review: Creative Production Mode (Post-Implementation Review)

**Reviewer:** Senior Unity / C# Systems & Architecture Reviewer  
**Context:** Independent Critical Second Review per `AGENTS.md`  
**Target:** [`implementation.diff`](review-context/implementation.diff), [`IBoardPointerSource.cs`](review-context/IBoardPointerSource.cs), [`CreativeScript.cs`](review-context/CreativeScript.cs), and [`CreativePointerScript.cs`](review-context/CreativePointerScript.cs)  
**Verdict:** **APPROVE WITH CHANGES**

---

### Executive Summary

The implementation faithfully adopts the majority of recommendations from the initial review:
1. **Release Event Completeness (#1):** Every exit from [`ReleasePiece()`](review-context/PuzzleDragController.cs#L1366-L1450) now captures `releasingPieceId` upfront and raises [`PieceReleased`](review-context/PuzzleDragController.cs#L67-L73) exactly once across all 5 code paths (joint-group snap success/failure, single-piece snap success, grid restore success, and restore fallback failure).
2. **Boundary Shadowing Elimination (#2):** [`CreativePointerScript`](review-context/CreativePointerScript.cs#L28-L58) strictly validates `moveFrames >= 1` and `drag.startFrame > releaseFrames[i - 1]`, completely preventing drag overlap and shadowing.
3. **Finite Pointer Lifetime (#3):** [`CreativePointerScript.IsActive`](review-context/CreativePointerScript.cs#L66-L68) evaluates `Time.frameCount - originFrame <= lastFrame`, cleanly releasing control back to touch/mouse input on `lastFrame + 1`.
4. **Lifecycle & Teardown (#5):** Subscriptions and pointer references are cleanly detached in [`OnDisable()`](review-context/PuzzleDragController.cs#L190-L195) and [`CreativeSession.End()`](review-context/CreativeSession.cs#L106-L115).
5. **Zero Normal-Play Overhead:** Normal play incurs only a single reference null-check per frame, with zero allocations and zero behavioural changes.

However, a **subtle but critical Unity lifecycle timing bug** exists between the coroutine delay in [`CreativeSession.StartScriptAfterDelay()`](review-context/CreativeSession.cs#L213-L237) and [`PuzzleDragController.Update()`](review-context/PuzzleDragController.cs#L244-L252). This causes frame `t = 0` to be skipped, which **silently drops any drag configured with `startFrame = 0`**.

---

### Prioritized Findings & Required Changes

#### 1. [CRITICAL] Frame `t = 0` Skipped Due to Coroutine Phase Execution (`startFrame = 0` Silently Dropped)
* **Files:** [`CreativeSession.cs`](review-context/CreativeSession.cs#L231-L236) and [`CreativePointerScript.cs`](review-context/CreativePointerScript.cs#L69-L95)
* **Problem:** In Unity's execution order, coroutines resuming from `yield return null` execute **after** [`Update()`](review-context/PuzzleDragController.cs#L244-L252) has already finished for that frame.
  Trace the execution:
  1. On frame $K$, [`PuzzleDragController.Update()`](review-context/PuzzleDragController.cs#L244-L252) runs while `scriptedPointerSource` is still null.
  2. Coroutines resume. The `for (int frame = 0; frame < config.Script.StartDelayFrames; frame++) yield return null;` finishes on frame $K$.
  3. [`StartScriptAfterDelay()`](review-context/CreativeSession.cs#L213-L237) calls `pointerScript.Start(camera, Time.frameCount)`, setting `originFrame = K`, and injects the source.
  4. Frame $K$ ends.
  5. On frame $K+1$, [`PuzzleDragController.Update()`](review-context/PuzzleDragController.cs#L244-L252) runs. `Time.frameCount` is now $K+1$.
  6. It calls `scriptedPointerSource.GetSample(Time.frameCount)`.
  7. In `GetSample`:
     ```csharp
     int t = frame - originFrame; // (K + 1) - K = 1
     ```
  8. **`t = 0` was NEVER sampled by the controller!**
* **Impact:**
  - If a designer authors Drag 0 with `startFrame = 0` (the natural default for "start dragging as soon as the script begins"), on the very first sampled frame ($t = 1$), `sample.Pressed = (t == startFrames[0])` evaluates to `(1 == 0)` which is **false**!
  - Because `sample.Pressed` is false, [`TrySelectPiece(pointer)`](review-context/PuzzleDragController.cs#L1206-L1214) is never called and `selectedPiece` remains null.
  - Furthermore, because `sample.Pressed` is false, the newly added `Debug.LogWarning` is **not triggered**.
  - Drag 0 fails completely and silently.
* **Required Change:**
  Decouple `originFrame` from the coroutine phase by lazily anchoring `originFrame` on the first call to [`GetSample()`](review-context/CreativePointerScript.cs#L78-L98) in [`CreativePointerScript.cs`](review-context/CreativePointerScript.cs):
  ```csharp
  private bool isStarted;

  public void Start(Camera camera)
  {
      for (int i = 0; i < startFrames.Length; i++)
      {
          fromScreen[i] = camera.WorldToScreenPoint(fromWorld[i]);
          toScreen[i] = camera.WorldToScreenPoint(toWorld[i]);
      }
      originFrame = -1;
      isStarted = true;
  }

  public void Stop()
  {
      isStarted = false;
      originFrame = -1;
  }

  public bool IsActive => isStarted && (originFrame < 0 || Time.frameCount - originFrame <= lastFrame);

  public BoardPointerSample GetSample(int frame)
  {
      if (originFrame < 0)
          originFrame = frame;

      int t = frame - originFrame;
      ...
  ```
  This guarantees that $t = 0$ is evaluated on the exact first frame [`PuzzleDragController.Update()`](review-context/PuzzleDragController.cs#L244-L252) consumes the source, regardless of caller timing.

---

#### 2. [LOW] `piece` ID Field Omitted in `CreativeEventLog` JSON for `PieceReleased`
* **File:** [`CreativeEventLog.cs`](review-context/CreativeEventLog.cs#L102-L105)
* **Problem:** In [`CreativeSession.OnPieceReleased`](review-context/CreativeSession.cs#L207-L211), `pieceId` is passed into `log.Add(CreativeEventKind.PieceReleased, pieceId, value: accepted ? 1 : 0)`.
  However, in `CreativeEventLog.cs`:
  ```csharp
  else if (e.Kind == CreativeEventKind.PieceReleased)
      json.Append(", \"accepted\": ").Append(e.Value != 0 ? "true" : "false");
  ```
  Unless `e.PieceId` is written by a shared prefix for all events, the resulting JSON log will output `{"frame": ..., "kind": "piece.released", "accepted": true}` without recording which piece was dropped.
* **Required Change:** Ensure `e.PieceId` (or `e.From`) is emitted in the JSON output:
  ```csharp
  else if (e.Kind == CreativeEventKind.PieceReleased)
      json.Append(", \"piece\": ").Append(e.From) // or e.PieceId
          .Append(", \"accepted\": ").Append(e.Value != 0 ? "true" : "false");
  ```

---

#### 3. [LOW] Defensive Validation: `drag.startFrame >= 0` and Catching General Exceptions in Coroutine
* **Files:** [`CreativePointerScript.cs`](review-context/CreativePointerScript.cs#L40-L46) and [`CreativeSession.cs`](review-context/CreativeSession.cs#L221-L226)
* **Problem:**
  1. [`CreativePointerScript`](review-context/CreativePointerScript.cs#L40-L46) validates `moveFrames < 1` and sequential order, but does not validate `drag.startFrame < 0` for Drag 0. (While `[Min(0)]` guards the Inspector, constructor validation ensures programmatic consistency).
  2. If `config.Level` is null (e.g. incomplete test asset), [`CellToWorld`](review-context/CreativePointerScript.cs#L99-L103) throws `NullReferenceException`, which escapes the `catch (ArgumentException)` block in [`StartScriptAfterDelay`](review-context/CreativeSession.cs#L213-L226).
* **Required Change:**
  - Add `if (drag.startFrame < 0) throw new ArgumentException($"Drag {i}: startFrame must be non-negative.");`
  - Catch `System.Exception` in [`StartScriptAfterDelay`](review-context/CreativeSession.cs#L221-L226) so asset authoring errors never break the take coroutine runner.

---

### Detailed Review of Mandated Areas

#### 1. Correctness of Every `ReleasePiece` Exit
Every branch of [`ReleasePiece()`](review-context/PuzzleDragController.cs#L1366-L1450) was traced line-by-line:
1. `int releasingPieceId = selectedPiece.SourcePieceId;` is cached at line 1366 before any branch can null `selectedPiece`.
2. **Joint Group Snap Success (line 1393):** `PieceReleased?.Invoke(releasingPieceId, true);` $\to$ Returns.
3. **Joint Group Snap Failure (line 1398):** `PieceReleased?.Invoke(releasingPieceId, false);` $\to$ Returns.
4. **Single-Piece Snap Success (line 1413):** `PieceReleased?.Invoke(releasingPieceId, true);` $\to$ Returns.
5. **Single-Piece Invalid Drop Restored (line 1433):** `PieceReleased?.Invoke(releasingPieceId, false);` $\to$ Returns.
6. **Failed Restore Fallback (line 1447):** `PieceReleased?.Invoke(releasingPieceId, false);` $\to$ Method ends.

**Conclusion:** Exactly one `PieceReleased` invocation is guaranteed on every possible release path. There are no duplicate invocations and no dropped events.

#### 2. Normal-Play Regressions (`scriptedPointerSource == null`)
- In [`Update()`](review-context/PuzzleDragController.cs#L244-L252), when `scriptedPointerSource == null`, the condition evaluates to `false` via a single reference comparison and falls directly through to `ProcessTouchInput()` / `ProcessMouseInput()`.
- `PieceReleased?.Invoke` checks for a null multicast delegate. In normal play with no listeners, this is an instantaneous null check.
- `OnDisable()` sets `scriptedPointerSource = null`.
- **Conclusion:** Zero behavioral or performance regressions in normal gameplay.

#### 3. Per-Frame Allocations & Hot Paths
- `BoardPointerSample` is a 16-byte value struct.
- [`GetSample()`](review-context/CreativePointerScript.cs#L78-L98) operates exclusively across preallocated primitive arrays (`int[]`, `Vector2[]`). No LINQ, no closures, no heap allocations.
- In [`ProcessScriptedInput()`](review-context/PuzzleDragController.cs#L1202-L1224), the newly introduced `Debug.LogWarning` is strictly guarded by `if (sample.Pressed && selectedPiece == null)`. Because `sample.Pressed` is true for at most 1 frame per drag, string formatting never runs in a per-frame loop.
- **Conclusion:** Complies fully with `AGENTS.md` per-frame allocation restrictions.

#### 4. Declining Finding #4 (Coordinate Round-Trip Rationale)
The implementer declined replacing `ScreenPosition` with `WorldPosition` in `BoardPointerSample`.
* **Evaluation:**
  - **Resolution Stability:** In Unity Recorder automated runs, Game View capture resolution is fixed before Play Mode starts and does not change during the take.
  - **Precision:** Float transformation through an orthographic camera matrix has a round-trip error under $10^{-6}$ units, which is completely negligible against unit cell spacing and the $0.5$ fine drag threshold.
  - **Parity & Presentation Hand:** Using screen coordinates keeps exact parity with [`ProcessMouseInput()`](review-context/PuzzleDragController.cs#L1160-L1195) and allows a canvas/screen-space presentation hand to directly track `sample.ScreenPosition`.
* **Conclusion:** **Declining #4 is fully justified and acceptable.**

#### 5. `AGENTS.md` Architectural & Contract Compliance
- **Composition Root:** [`RuntimePieceFactoryBootstrap`](review-context/RuntimePieceFactoryBootstrap.cs#L244-L248) explicitly injects `puzzleDragController` into [`CreativeSession`](review-context/CreativeSession.cs#L35-L55). No singletons or runtime service lookups are used.
- **Single Authority:** Reuses [`PuzzleDragController`](review-context/PuzzleDragController.cs#L63-L85) and standard board grid validation rather than inventing a parallel simulated input pipeline.
- **Minimising Update Runners:** [`IBoardPointerSource`](review-context/IBoardPointerSource.cs#L22-L28) is a passive, pure frame-sampling interface requiring no `MonoBehaviour.Update()` runner.
- **Namespace and Project Layout:** All new interfaces and classes reside in appropriate namespaces (`GravityPuzzle.Gameplay.Input`, `GravityPuzzle.Creative.Config`, `GravityPuzzle.Creative.Runtime`).

---

### Final Verdict

**APPROVE WITH CHANGES**

The architecture and implementation are high-quality, well-structured, and strictly compliant with `AGENTS.md`. Once **Finding 1** is resolved (lazily anchoring `originFrame` in [`CreativePointerScript`](review-context/CreativePointerScript.cs#L69-L95) to prevent skipping $t = 0$), this implementation is ready for production merge.
