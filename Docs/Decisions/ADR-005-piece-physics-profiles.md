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
`PrototypeBoard.TryBeginOpenFreefall` owns the one grid-to-off-board transition
and clears the reservation there. `PuzzlePiece` owns all normal-piece physics
profiles:

* `Grid`: deterministic kinematic representation with queryable colliders for
  `PuzzleDragController` selection; movement remains grid-authoritative.
* `OpenFreefall`: dynamic, full collision geometry, and the dedicated
  `ShredderPhysicsFreefall` layer. Its matrix permits contact only with the same
  layer.
* `Shredding`: Rigidbody simulation and colliders disabled; the feed is
  presentation-only.

Temporary support-island profiles remain separate legacy corridor behavior and
are never used by the map-exit freefall path.

Pooling re-applies the Grid profile on spawn and despawn so layers, materials,
velocities, constraints, colliders, and simulation state cannot leak.

## Consequences

Open freefall pieces may stack and rotate naturally only against one another.
They cannot use map or mouth geometry as invisible support. Changes to freefall
contacts require both the dedicated layer and `ShredderPhysicsLayers.Configure`;
they must not be made by mutating individual pieces at runtime.
