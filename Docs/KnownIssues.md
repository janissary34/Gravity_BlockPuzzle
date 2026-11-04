# Known Issues

Only verified unresolved issues belong here. Update or remove an entry in the same change that resolves it.

## Joint spring placement for uncalibrated sprites

The `BrickBlocks` atlas layouts have measured stud positions, but sprites outside that
atlas and non-simple renderers may still fall back to legacy seam-offset placement.
The manually adjusted `Spring Calibration Example` supplied approximate reference-fit
points. Runtime alignment, including vertical joins, has not yet been visually validated.
Editing the example does not automatically update those reference-fit points.

## Runtime-created pin objects

Status: architectural debt; current behaviour is functional.

Board background, frame, map collision, obstacles, and reveal presentations now
use authored assets or prewarmed typed pools (`LevelRuntimeRoot`, `RevealAreaPool`,
`JointSpringPool`). Generated pin objects still retain their older runtime-construction paths.

Do not extend that path. Migrate pin objects to authored or prewarmed adapters in a
separate change while preserving pin collision behaviour.

## Creative Mode: `Frozen` timer mode does not freeze the timer

Status: verified on branch `marketing` (take `gbp_L30_20261104-164645`; the timer counts down from 05:00 to 04:43).
- `PrototypeBoard.StartTimer` calls `TryTransitionGameState(Playing)`, which raises `GameStateChanged`, before it sets `IsTimerStarted`.
- `CreativeSession` requests its pause from that event, so `TryPauseTimer` sees an inactive timer and returns false. The pause is never applied.
- Affects only creative takes with `timerMode: Frozen`; normal play is unaffected.

## Stale regression tests (pre-existing on `main`)

Status: verified 2026-11-04. The suites were run in batch mode on clean checkouts of `main` (`1addb3bc`) and `marketing` (`83019fd7`). Both show the same 14 failures with identical messages; `marketing` adds 4 passing Creative timer tests. These are test-fixture drift, not observed gameplay defects:

- **`BoxGravityRegressionPlayModeTests` (4: IndependentPieces…, JointGroup…, LockedBox…, MidAirClosedBox…):** `NullReferenceException` in the test body. `GridCoordinate.X/Y` became public readonly fields in `00d05c1c`, but the tests still call `GetProperty("Y")`, which returns null.
- **`FineGridDragStepperPlayModeTests` (7 `RealController_*`):** `PuzzleDragController.Start()` logs `[PuzzleDrag] No gameplay camera is configured…`. The fixture never configures a camera, so the Test Framework fails each test on the unexpected error log before any drag assertion runs. These tests therefore give no drag coverage until the fixture is updated.
- **`BombPresentationTests` (EditMode, 3):**
  - `ArmedBomb_ShredderPresentation…`: the bomb overlay is still in the voxel source list;
  - `BoosterRewardContent_Reactivates…`: `Expected: True, But was: False` at line 49;
  - `HammerHitOnArmedBomb…`: `PuzzlePiece.UpdateBombCounter` uses `renderer.material` in Edit Mode (material leak error).

**Fixes belong on `main`** (test fixtures: `GetField`, a configured camera or an expected log; the bomb presentation expectations), not on the Creative Mode branch.
