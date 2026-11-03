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

## Joined piece hangs while its partner is in freefall

Status: root cause verified in code; not reproduced on device.

Map-exit capture (`BlockShredder.TryCapturePiece`) moves one joint-group member to
`OpenFreefall` without breaking its joints. Joints break only at cutter capture
(`BlockShredder.TryCaptureFreefallPieceAtCutter`). Joint-group gravity requires every
member to be `Placed` (`PuzzleDragController.cs:687`), so the members still on the
board stay suspended until the freefall member reaches the cutter. This explanation
does not cover the same symptom on unjoined pieces.

## Mid-drag shredder capture strands joint-group members in Dragging

Status: root cause verified in code; not reproduced on device.

Starting a drag moves every joint-group member to `Dragging`
(`PrototypeBoard.TryBeginPieceDrag`, `PrototypeBootstrap.cs:1098-1102`). When the held
member is captured at the map exit, `PuzzleDragController` only drops its selection
(`PuzzleDragController.cs:197-201`, `334-338`), and `CancelGridFallForTargetedAction`
returns only the captured piece to `Placed` (`:129`). Grid gravity plans only `Placed`
pieces (`:687`, `:707`), so the remaining members do not fall until touched again.

## Booster buttons can subscribe to a discarded inventory

Status: root cause verified in code; not reproduced on device.

`BoosterInventoryRuntime.Current` lazily creates an inventory on first access
(`BoosterInventoryService.cs:119-120`), and `RuntimePieceFactoryBootstrap.Awake`
unconditionally replaces it (`RuntimePieceFactoryBootstrap.cs:101`). Booster UI
subscribes to `CountChanged` in `OnEnable` (`BoosterButton`, `RocketBooster`,
`HammerBooster`, `FreezeTimerBooster`, `BoosterTargetingPresentation`). All these
scripts use the default execution order, so a component enabled before the bootstrap
keeps listening to the replaced instance and misses count updates.
