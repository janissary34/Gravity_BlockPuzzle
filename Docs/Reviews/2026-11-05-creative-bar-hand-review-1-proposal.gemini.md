# Independent Critical Technical Review

**Review Subject:** Proposal — Progress Bar Placement Fix with Pre-Recording Validation & Creative Hand After Effects Overlay  
**Reviewer:** Independent Senior Technical Reviewer  
**Context:** Headless isolated evaluation against `AGENTS.md`, `CreativeSession.cs`, `CreativePointerScript.cs`, `CreativeEventLog.cs`, `takes.py`, and the creative pipeline resolvers.

---

## Executive Summary

The proposal addresses two primary requirements:
1. **Part A (Progress Bar):** Correcting the stale canvas camera transform bug during creative take initialization, anchoring the bar relative to the true board frame top rather than grid rows, and enforcing pre-recording validation that halts capture on failure.
2. **Part B (Creative Hand):** Exporting pointer motion samples from Unity during scripted takes and generating an After Effects overlay via a new `hand` spec block rather than burning hand visuals into the gameplay video.

The fundamental architectural directions are sound:
- Post-render placement in `SignalCaptureReadyAfterDelay` correctly resolves the Unity Screen Space – Camera canvas lifecycle race condition.
- Generating the hand as an After Effects overlay preserves pure gameplay capture footage and leaves gameplay state, timing, and physics completely untouched.

However, the proposal contains critical gaps concerning pipeline clip speed/time remapping synchronization, semantic breaking changes to serialized framing profiles, dependency wiring in `CreativeSession`, and potential GC allocations in hot paths. 

---

## Findings by Severity

### HIGH SEVERITY (Risk of Broken Builds, Sync Failures, or Incompatibilities)

#### [H1] Hand Overlay Resolver Ignores Clip Speed, Time Remapping (`hold_from`), and Fractional Frame Rates
- **Location:** Proposal Section B (Items 10–11), `resolve.clip.excerpt.py`, `resolve.events.excerpt.py`.
- **Finding:** The proposal specifies: *"Keys: per-frame position during a drag, exactly from samples... drops drags outside the block span... Reverse clips are refused."* However, timeline clips in this pipeline frequently have non-unity playback speeds (`speed != 1`), freeze frames (`hold_from`), or frame rate differences between capture (e.g., 60 fps) and comp (e.g., 30 fps).
  - If a clip has `speed = 1.25`, mapping take sample frames directly to comp frames causes samples to land on sub-frames (`Fraction(master).denominator != 1`), resulting in rounding jitter or sub-frame keys.
  - If a clip utilizes `hold_from` (freeze frame), the video freezes while the proposed sample-driven hand overlay would continue animating across a stationary board.
  - If comp fps differs from capture fps, 1 sample per take frame does not correspond 1:1 with 1 key per comp frame.
- **Contract Impact:** Breaks predictable presentation sync between gameplay footage and the overlay.
- **Required Change:** The resolver must map comp master frames into clip source time (sampling or interpolating between adjacent pointer samples), clamp hand motion when `hold_from` is active, and cleanly handle fractional speed scaling.

#### [H2] Semantic Breaking Change to `CreativeFramingProfile.ProgressBarBoardGap`
- **Location:** Proposal Section A (Item 3), `CreativeFramingProfile.cs`.
- **Finding:** Currently, `ProgressBarBoardGap` represents the distance from the board top to the *centre* of the progress bar (typically tuned around 0.10–0.15). Item 3 redefines this exact field to mean: *"frame top to the bottom of the lowest visible element attached to the bar... Value ~0.02."*
  - Reinterpreting an existing serialized ScriptableObject field breaks all existing `CreativeFramingProfile` assets in the repository. If an existing profile with `gap = 0.12` is executed under the new logic, the bar will be pushed significantly higher off-screen or trigger the new safe-area validation failure.
- **Contract Impact:** Violates the Compatibility Rule (*"preserve established public contracts unless a change is necessary, update all affected callers when a contract intentionally changes"*).
- **Required Change:** Either introduce an explicit migration/versioning mechanism, create a dedicated field, or update all existing profile assets alongside the tooltip documentation in `CreativeFramingProfile.cs`.

---

### MEDIUM SEVERITY (Architectural Inconsistencies & Hidden Runtime Risks)

#### [M1] Missing Dependency Injection Path for `TryGetBoardFrameTop` in `CreativeSession`
- **Location:** Proposal Section A (Item 1), `CreativeSession.cs` constructor.
- **Finding:** Item 1 introduces `LevelRuntimeRoot.TryGetBoardFrameTop` forwarded by `GravityLevelRuntime.TryGetBoardFrameTop`. However, `CreativeSession` does not hold a reference to `GravityLevelRuntime` or `LevelRuntimeRoot`—its constructor only accepts `(config, request, board, dragController, camera, bindings, inventory, coroutineHost)`.
- **Contract Impact:** Violates `AGENTS.md` Dependency Rules (*"Dependencies should be supplied explicitly from the composition root whenever practical. Avoid hidden runtime dependency discovery"*).
- **Required Change:** Explicitly pass `GravityLevelRuntime` (or a dedicated interface/accessor) into `CreativeSession` from `PrototypeBootstrap` at session creation.

#### [M2] Event Log Fixed Capacity (1024) Causes Hot-Path GC Allocation
- **Location:** Proposal Section B (Items 7–8), `CreativeEventLog.cs`, `CreativeSession.cs`.
- **Finding:** `CreativeSession` instantiates `log = new CreativeEventLog(1024)`. In scripted takes with contact logging enabled every frame, a 20–30s take with multiple drags can easily log 1,200–1,800 `pointer.sample` events in addition to lifecycle events. Once count exceeds 1,024, the underlying `List<Entry>` will double its array and allocate on the main thread during gameplay dragging.
- **Contract Impact:** Violates `AGENTS.md` Performance Rules (*"Avoid per-frame allocations... Preallocate hot-path collections"*).
- **Required Change:** Scale the preallocated `CreativeEventLog` capacity during session initialization: `Math.Max(1024, (int)(config.MaxSeconds * config.CaptureFrameRate) + 256)`.

#### [M3] `Screen.safeArea` in Unity Editor Game View Does Not Reflect Mobile Notches
- **Location:** Proposal Section A (Item 5).
- **Finding:** Item 5 specifies failing validation if the element bounds fall *"outside the safe area (Screen.safeArea fractions applied to the camera pixel rect)"*. In the desktop Unity Editor Game View (macOS/Windows), `Screen.safeArea` always returns `(0, 0, Screen.width, Screen.height)`. It will detect elements extending completely outside the camera viewport, but it will *not* detect violations of device notch/home-indicator insets unless Device Simulator is explicitly running.
- **Required Change:** Clarify that safe area checking in Editor Game View validates camera viewport containment (0..1) unless explicit simulated margins are provided via the framing profile.

#### [M4] Boundary Glitches on 1-Frame Drag Gaps and Clip In/Out Spans
- **Location:** Proposal Section B (Items 10–11), `CreativePointerScript.cs`.
- **Finding:**
  - `CreativePointerScript` permits `startFrame == releaseFrames[i - 1] + 1` (a 1-frame gap between consecutive drags). Item 11 specifies: *"If the gap between drags is too short for exit+approach, an eased glide replaces them."* A 1-frame glide is an instantaneous coordinate jump that also toggles `pressed -> released -> pressed` within a single frame, which can produce visual pops in After Effects.
  - If a drag starts at the very beginning of a clip (`startFrame < approach_frames`), the approach keyframes would extend before `clip.start_f`.
- **Required Change:** Define clamping rules for approach/exit at clip boundaries and specify minimum hold/glide thresholds for 1-frame gaps.

#### [M5] Incomplete Null-Check in `LevelRuntimeRoot.TryGetBoardFrameTop`
- **Location:** Proposal Section A (Item 1), `LevelRuntimeRoot.excerpt.cs`.
- **Finding:** In Unity, if a `SpriteRenderer` is enabled but has `sprite == null`, querying `renderer.bounds` returns `Bounds(transform.position, Vector3.zero)`. If an empty slot happens to be enabled, `bounds.max.y` will evaluate to the transform's Y coordinate rather than visual sprite extents.
- **Required Change:** Ensure the iteration filters for `renderer != null && renderer.enabled && renderer.sprite != null`.

---

### LOW SEVERITY & CLEANLINESS (Code Quality & Lifecycle)

#### [L1] Side Effect in `IBoardPointerSource.GetSample`
- **Location:** Proposal Section B (Item 8), `CreativePointerScript.cs`.
- **Finding:** Placing event logging inside `GetSample(int frame)` embeds side effects into a getter query. While scoped to scripted takes, if `GetSample` is called multiple times within a frame (e.g., in tests or debug inspection), it would log redundant samples unless guarded.
- **Required Change:** Ensure `GetSample` guards logging against multiple invocations in the same frame (`if (frame != lastLoggedFrame)`).

#### [L2] Schema and Enum Alignment in `CreativeEventLog`
- **Location:** Proposal Section A (Item 5) & Section B (Item 7), `CreativeEventLog.cs`.
- **Finding:** `CreativeEventKind` and `KindNames` must add `PointerSample = 12` (`"pointer.sample"`) and `CaptureFailed = 13` (`"capture.failed"`).
- **Required Change:** Ensure additions are appended additively to preserve enum numbering and documentation schema compatibility.

#### [L3] Placeholder Asset License Tracking
- **Location:** Proposal Section B (Item 9).
- **Finding:** Hand assets imported with license `to-confirm` and marked "internal testing only" must not leak into production releases without validation.
- **Required Change:** Document the asset license requirement in `Docs/KnownIssues.md` or `Docs/Decisions/` in accordance with repository documentation policy.

---

## Evaluation Against AGENTS.md Contract Rules

| Rule / Requirement | Evaluation | Notes |
| :--- | :--- | :--- |
| **Normal Gameplay Invariance** | **PASSED** | Normal play does not instantiate `CreativeSession`, calls no frame-top queries, and uses no scripted pointers. |
| **Physics / Movement Invariance** | **PASSED** | Board physics, piece settling, coordinate transforms, and drag input coordinates remain 100% identical. |
| **No Runtime Instantiate/Destroy** | **PASSED** | Uses existing pools and preallocated structs; no dynamic component additions or allocations. |
| **Presentation vs Gameplay Separation** | **PASSED** | Hand is rendered entirely outside Unity via AE; progress bar is presentation-only. |
| **Explicit Dependencies & Composition Root** | **NEEDS WORK** | `CreativeSession` needs explicit injection of `GravityLevelRuntime` from `PrototypeBootstrap` (Finding M1). |
| **Hot-Path Allocations** | **NEEDS WORK** | `CreativeEventLog` initial capacity must be increased to avoid runtime resizing during drags (Finding M2). |
| **Compatibility Rule** | **NEEDS WORK** | Redefining `ProgressBarBoardGap` impacts existing profiles without migration (Finding H2). |

---

## Simpler Alternatives Considered

1. **Progress Bar Placement via World Canvas:**  
   Changing the HUD canvas from `ScreenSpaceCamera` to world space would avoid camera render delay, but it would require extensive prefab re-authoring and break production UI rendering. Moving placement to `SignalCaptureReadyAfterDelay` (as proposed) is the cleaner, non-invasive solution.
2. **Logging Pointer Positions in `PuzzleDragController`:**  
   Logging in `PuzzleDragController` would record the consumed position directly, but it would pollute the core gameplay assembly (`Gameplay/Input/`) with presentation/creative logging dependencies. Keeping it inside `CreativePointerScript` maintains modular separation, provided it is idempotent per frame.

---

## Verdict

### **APPROVE WITH CHANGES**

The proposal is approved for implementation subject to incorporating the following required modifications:

1. **Resolve Resolver Time/Speed Mapping ([H1]):** Define timeline comp sampling in the After Effects resolver to properly handle non-unity clip playback speeds (`speed != 1`), clip in/out boundaries, and `hold_from` freeze frames.
2. **Preserve or Migrate Framing Configs ([H2]):** Prevent regressions in existing `CreativeFramingProfile` assets by either adding a new explicit property/mode or performing a simultaneous migration and documentation update of all existing framing profiles.
3. **Explicit Dependency Injection ([M1]):** Pass `GravityLevelRuntime` (or a dedicated interface) explicitly into `CreativeSession` via `PrototypeBootstrap`.
4. **Preallocate Event Log Capacity ([M2]):** Calculate initial `CreativeEventLog` capacity from `config.MaxSeconds * config.CaptureFrameRate` to ensure zero allocations during recorded drags.
5. **Guard Sprite and Frame Bounds ([M4], [M5]):** Verify `renderer.sprite != null` in `TryGetBoardFrameTop` and clamp hand approach/exit keys to valid clip bounds.
