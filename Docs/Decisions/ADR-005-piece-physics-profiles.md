# ADR-005: Isolate piece physics by lifecycle phase

## Status

Accepted

## Context

The old map-exit freefall path entered through the temporary shredder-support
setup. It therefore inherited the support layer, feed material, and contacts
with map blockers and mouth guides. Dynamic pieces could slow, rotate, or be
depenetrated before reaching the cutter.

## Decision

The lifecycle explicitly uses `HandoffToPhysics -> OpenFreefall -> Shredding`.
`HandoffToPhysics` is an atomic transaction boundary, not a visible movement
phase: `PrototypeBoard` clears the reserved grid footprint and transitions to
`OpenFreefall` at the same map-exit boundary. `PieceModel` is the sole lifecycle
authority, and `PuzzlePiece.ApplyLifecyclePhysicsProfile` is the only Unity-side
profile writer:

* `Grid`: non-simulated representation with colliders disabled; drag selection
  resolves an authoritative occupied grid cell and movement remains grid-authoritative.
* `OpenFreefall`: dynamic, full collision geometry, and the dedicated
  `ShredderPhysicsFreefall` layer. Its matrix permits contact only with the same
  layer.
* `Shredding`: Rigidbody simulation and colliders disabled; the established
  controlled cutter feed is presentation-only.

Grid drag, gravity presentation, and release snap move transforms through
`PieceGridFallView`; they never tween a Rigidbody2D. At presentation completion,
`PuzzlePiece.SetGridPresentationPosition` synchronizes the dormant body pose with
the committed grid transform before a later physics handoff. The view records
the latest authored downward presentation rate; map exit transfers that speed
unchanged to OpenFreefall. The shredder config is only a fallback for a path
without a measurable downward grid presentation.

Pooling re-applies the Grid profile on spawn and despawn so layers, materials,
velocities, constraints, colliders, and simulation state cannot leak.

## Consequences

Open freefall pieces may stack and rotate naturally only against one another.
They cannot use map or mouth geometry as invisible support. Changes to freefall
contacts require both the dedicated layer and `ShredderPhysicsLayers.Configure`;
they must not be made by mutating individual pieces at runtime. No lifecycle
phase may use Physics2D collider ordering to select a grid piece.

The cutter captures a freefall body at its rendered seam without moving it to a
shared seam. The body stays Dynamic and simulated, changes only to the dedicated
`ShredderPhysicsFeed` layer, and keeps its colliders enabled. Freefall and feed
layers can collide only with each other, never with the map, blockers, guides, or
normal board. This preserves vertical support between cutting pieces while their
existing linear velocity continues unchanged. The feed presentation observes the
live body from `BlockShredder`'s single fixed-step feed list to clip visuals,
emit debris and report progress; it never writes the piece transform, velocity,
rotation, colliders, layers, or grid occupancy.
