# Independent Critical Technical Review: Implementation Analysis

**Review Subject:** Implemented Changes for Creative Progress Bar Placement, Pre-Recording Validation & Creative Hand Overlay  
**Reviewer:** Independent Senior Technical Reviewer  
**Context:** Headless, read-only evaluation against `AGENTS.md`, `review-1-proposal.md`, `DECISIONS-AND-VALIDATION.md`, repository diffs (`game.diff`, `pipeline.diff`), and associated test suites.

---

## Executive Summary

This implementation addresses the two core features scoped in Review 1:
1. **Progress Bar Placement & Pre-Recording Validation:** Correcting the stale camera transform bug via deferred post-render placement (`SignalCaptureReadyAfterDelay`), measuring true visual frame extents via `LevelRuntimeRoot.TryGetBoardFrameTop`, accounting for timer bounding boxes and frame art transparent padding, and stopping capture if bounds overlap the board or violate screen safety margins.
2. **Creative Hand Overlay:** Capturing high-fidelity pointer samples (`pointer.sample`) during scripted takes in Unity and compiling an editable, style-driven After Effects hand overlay track in the video pipeline (`cpl/hand.py`, `hand.jsxinc`, and `cpl/resolve.py`).

The implementation adheres closely to the approved proposal and faithfully incorporates the decisions made on Review 1 findings (H1, H2, M1–M5, L1–L3). Normal gameplay remains completely unaffected. Core physics, grid rules, piece state flow, and input sampling are untouched.

However, several defects and test gaps warrant remediation: a partial mutation bug in `PrepareProgressBar` violating `AGENTS.md` state integrity rules, an opacity key collision on short approach intervals in `build_keys`, a fallback flaw in `_interpolate`, and missing test coverage for timer bounding box inclusion.

---

## Review 1 Tracking & Proposal Compliance

| Review 1 Finding | Status in Implementation | Verification Evidence |
| :--- | :--- | :--- |
| **[H1] Speed & Hold Handling** | **ADOPTED** | Hand samples are evaluated per master frame via `ClipMap.source_at` with fractional interpolation in `master_strokes`. Reverse clips (`speed <= 0`) are rejected, and `hold_from` freezes/drops subsequent drag samples. |
| **[H2] Profile Gap Semantics** | **ADOPTED** | `CreativeFraming_BoardCentered.asset` was updated (`0.05 -> 0.015`), new field `boardFrameArtPadding` added (`0.3`), and tooltips in `CreativeFramingProfile.cs` document the new semantics. |
| **[M1] Bootstrap Injection** | **ADOPTED** | `LevelRuntimeRoot` is explicitly passed from `RuntimePieceFactoryBootstrap` into the `CreativeSession` constructor without runtime discovery. |
| **[M2] Event Log Capacity** | **ADOPTED** | `CreativeEventLog` preallocates `Mathf.Max(1024, Mathf.CeilToInt(config.MaxSeconds * config.CaptureFrameRate) + 256)` preventing hot-path GC allocations during dragging. |
| **[M3] Safe Area In Editor** | **ADOPTED** | Documented in `Docs/CreativeMode.md` and code comments that Editor Game View safe area corresponds to the full viewport. |
| **[M4] Boundary Clamping** | **ADOPTED** | Approach/exit keys in `cpl/hand.py` are clamped to `[start_f, end_f - 1]`. Consecutive drags with gaps `< min_glide_f` maintain the pressed finger state. |
| **[M5] Renderer Null-Check** | **ADOPTED** | `LevelRuntimeRoot.TryGetBoardFrameTop` checks `renderer != null && renderer.enabled && renderer.sprite != null`. |
| **[L1] Idempotent Pointer Log** | **ADOPTED** | `CreativePointerScript.LogSample` guards against duplicate frame queries (`frame == lastLoggedFrame`). |
| **[L2] Event Enum Alignment** | **ADOPTED** | `CaptureFailed = 12` and `PointerSample = 13` are appended additively with corresponding string identifiers in `KindNames`. |
| **[L3] Asset License Tracking**| **ADOPTED** | Recorded in `PIPELINE.md` under reference art guidelines. |

---

## Findings by Severity

### MEDIUM SEVERITY (State Integrity & Contract Violations)

#### [M1] Partial Mutation on Validation Failure in `PrepareProgressBar`
- **Location:** `Assets/Scripts/Creative/Runtime/CreativeSession.cs` (lines 194–226).
- **Finding:** In `PrepareProgressBar`, when `framed` is true, the bar's world position is mutated immediately:
  ```csharp
  originalProgressBarPosition = bar.localPosition;
  progressBarMoved = true;
  bar.position += new Vector3(0f, toWorld.y - fromWorld.y, 0f);
  ```
  Immediately following this translation, validation checks whether the placed rect overlaps the frame (`placed.yMin <= frameTop`) or exceeds the safe screen area (`safe`). If either check fails, the method returns `false` with `failure` populated, but **does not restore `bar.localPosition`**.
  
  While `SignalCaptureReadyAfterDelay` proceeds to log `CaptureFailed` and call `Complete()`, leaving Play Mode in the standalone editor, any test or caller executing `PrepareProgressBar` leaves the scene UI in a corrupted/mutated state. Furthermore, if `Complete()` is customized or delayed, the bad layout remains active on screen.
- **Contract Impact:** Violates `AGENTS.md` **State and data integrity** rule:
  > *"Invalid states should fail safely, warn clearly, and avoid partial mutation."*
- **Required Change:** Revert `bar.localPosition` before returning `false` on validation failure:
  ```csharp
  if (placed.yMin <= frameTop)
  {
      RestoreProgressBar();
      failure = $"the progress bar overlaps the board: ...";
      return false;
  }
  if (placed.xMin < safe.xMin || placed.xMax > safe.xMax || placed.yMin < safe.yMin || placed.yMax > safe.yMax)
  {
      RestoreProgressBar();
      failure = $"the progress bar is outside the safe screen area: ...";
      return false;
  }
  ```

---

### LOW SEVERITY (Edge Cases, Precision & Robustness)

#### [L1] Dictionary Key Collision Causes Premature Opacity Wipe on Short Approaches
- **Location:** `cpl/hand.py` (lines 112–120).
- **Finding:** In `build_keys`:
  ```python
  if not glide_in:
      begin = max(start_f, press_f - approach)
      if begin < press_f:
          position[begin] = (first[1] + offset[0], first[2] + offset[1], True)
          fade_end = min(begin + FADE_F, press_f)
          released[begin] = (0, "linear")
          released[fade_end] = (full, "hold")
  if not held_in:
      pressed[press_f] = (full, "hold")
      released[press_f] = (0, "hold")
  ```
  If a drag begins within 6 frames of the approach start (`press_f - begin <= FADE_F`), `fade_end` evaluates to `press_f`. Consequently, line 117 assigns `released[press_f] = (full, "hold")`, which is immediately overwritten on line 120 by `released[press_f] = (0, "hold")`.
  
  Because dictionary keys are unique frames, `released` ends up with keys `[begin, 0, "linear"]` and `[press_f, 0, "hold"]`. Instead of fading in over the available frames, the hand remains at 0% opacity throughout the entire approach glide and suddenly pops into view as the `pressed` sprite at `press_f`. Note that exit handling (line 130) explicitly guarded against this boundary collision (`if fade_start > release_f`), but approach handling does not.
- **Required Change:** Ensure `fade_end` does not collide with `press_f` when setting `full` opacity, or start `fade_end` at `press_f - 1` if `press_f - 1 > begin`.

#### [L2] Boundary Fallback in `_interpolate` Returns Final Sample When Queried Before First Sample
- **Location:** `cpl/hand.py` (lines 48–54).
- **Finding:** `_interpolate` searches for a segment spanning `frame`:
  ```python
  for (f0, x0, y0), (f1, x1, y1) in zip(samples, samples[1:]):
      if f0 <= frame <= f1:
          k = (frame - f0) / Fraction(f1 - f0)
          return x0 + (x1 - x0) * k, y0 + (y1 - y0) * k
  return samples[-1][1], samples[-1][2]
  ```
  If `frame < samples[0][0]`, the loop completes without a match and falls through to `return samples[-1][1], samples[-1][2]`, returning the position of the **last** sample instead of the **first**.
  
  While `master_strokes` currently clamps `first = max(start_f, _ceil(cmap.master_at(samples[0][0])))`, relying on `samples[-1]` as the universal fallback makes `_interpolate` non-monotonic and dangerous if invoked near the beginning of a clip with non-integer time mapping.
- **Required Change:** Add an explicit boundary clamp at the beginning of `_interpolate`:
  ```python
  if frame <= samples[0][0]:
      return samples[0][1], samples[0][2]
  ```

#### [L3] Unchecked Clip `crop` Transform in `clip_placement`
- **Location:** `cpl/hand.py` (lines 31–45).
- **Finding:** `clip_placement` inspects `rotation`, `anchor`, and `fit`, raising a `SpecError` if non-standard values are found. However, it does not check for `block.get("crop")`. If a gameplay clip is cropped, its visible frame and pixel center in AE shift, but `clip_placement` will still calculate standard uncropped coordinates, resulting in hand drift relative to the cropped video.
- **Required Change:** Either reject cropped clips explicitly (`if block.get("crop"): raise SpecError(path, "a hand cannot follow a cropped clip")`) or incorporate crop offsets into `cx, cy, width, height`.

---

## Test Coverage & Validation Gaps

1. **Timer Inclusion in `AttachedScreenRect` Has No Automated Test:**  
   The primary driver for shifting from bar center positioning to `AttachedScreenRect` was preventing the HUD Timer (which hangs beneath the progress bar) from colliding with the board. In `CreativeFramingPlayModeTests.cs`, only `progressBar` and `progressBarVisual` are instantiated in the test harness; no `CreativeSceneBindings.UIGroupBinding` for `CreativeUIGroup.Timer` is authored. As a result, the loop traversing timer graphics in `AttachedScreenRect` is never executed during automated PlayMode tests.
2. **Pre-Recording Validation Failure Path Stalls Test Runner:**  
   As noted in `DECISIONS-AND-VALIDATION.md`, calling `Session.Begin()` on an invalid configuration triggers `Complete()`, which attempts to exit Play Mode and stalls Unity's test runner. Consequently, all 4 negative validation tests call private `PrepareProgressBar` directly via reflection rather than exercising the true coroutine lifecycle (`SignalCaptureReadyAfterDelay` -> `log.Add(CaptureFailed)`). A unit test verifying that `CaptureFailed` is properly dispatched without hanging should be added.
3. **Pointer Event JSON Serialization Omitted from C# Unit Tests:**  
   `CreativePointerLogTests.cs` verifies sample counts and idempotent frame queries, but does not assert that `CreativeEventLog.Write(...)` correctly formats `"pointer.sample"`, `"phase"`, `"x"`, and `"y"` into valid JSON lines.

---

## Evaluation Against AGENTS.md Contract Rules

| Rule / Requirement | Evaluation | Notes |
| :--- | :--- | :--- |
| **Normal Gameplay Invariance** | **PASSED** | Normal play uses none of the creative methods, pointers, or framing calculations. |
| **Zero Runtime Allocations** | **PASSED** | Struct-based `CreativeEventLog` preallocates capacity; `AttachedScreenRect` reuses cached `Vector3[4]` buffer. |
| **Explicit Dependencies** | **PASSED** | `LevelRuntimeRoot` injected explicitly from `RuntimePieceFactoryBootstrap`. No runtime discovery. |
| **State and Data Integrity** | **NEEDS WORK** | `PrepareProgressBar` mutates `bar.position` prior to validating bounds and fails without rolling back (Finding [M1]). |
| **Tween & Presentation Isolation**| **PASSED** | Presentation does not mutate board occupancy; hand overlay is calculated entirely off-device for AE. |
| **Documentation Integrity** | **PASSED** | Tooltips, `Docs/CreativeMode.md`, `ARCHITECTURE.md`, `PIPELINE.md`, and `HANDOFF.md` are synchronized. |

---

## Verdict

### **APPROVE WITH CHANGES**

The implementation is well-architected, cleanly separated across repositories, and accurately fulfills the proposal and decisions made in Review 1. Approval is granted subject to resolving the following items before merging to main:

1. **Rollback Partial Mutation on Validation Failure ([M1]):** Call `RestoreProgressBar()` before returning `false` in `PrepareProgressBar`.
2. **Fix Boundary Fallback & Opacity Overwrite ([L1], [L2]):** Add `frame <= samples[0][0]` guard to `_interpolate`, and prevent `released[press_f]` from wiping out `fade_end` when `press_f - begin <= FADE_F`.
3. **Guard Against Cropped Clips ([L3]):** Validate that clips targeting a `hand` block do not contain unsupported `crop` transforms.
4. **Expand Test Coverage:** Add an automated test case exercising `AttachedScreenRect` with an active/inactive Timer graphic.
