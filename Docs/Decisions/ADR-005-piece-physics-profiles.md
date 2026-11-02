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

Pooling re-applies the Grid profile on spawn and despawn so layers, materials,
velocities, constraints, colliders, and simulation state cannot leak.

## Consequences

Open freefall pieces may stack and rotate naturally only against one another.
They cannot use map or mouth geometry as invisible support. Changes to freefall
contacts require both the dedicated layer and `ShredderPhysicsLayers.Configure`;
they must not be made by mutating individual pieces at runtime. No lifecycle
phase may use Physics2D collider ordering to select a grid piece.

The cutter captures a freefall body at its rendered seam, disables physics, and
performs at most one seam-alignment correction before presentation begins. The
feed coroutine owns visual clipping, jitter, debris, and pooling only; it does
not mutate colliders, layers, Rigidbody2D settings, or grid occupancy.
