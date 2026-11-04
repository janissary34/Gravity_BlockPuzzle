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
