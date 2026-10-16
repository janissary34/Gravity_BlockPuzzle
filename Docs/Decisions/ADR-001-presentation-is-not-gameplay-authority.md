# ADR-001: Presentation Is Not Gameplay Authority

Status: accepted.

## Context

Pieces and obstacles can switch between individual modules, exact atlas silhouettes, joined fallback bars, ice overlays, bomb overlays, dissolve effects, and shredder clipping. These representations may be hidden, replaced, pooled, or animated during play.

Using renderer bounds or transforms as authoritative occupancy would make gameplay depend on presentation refresh order and would allow visual defects to corrupt placement, gravity, hammer, and shredder rules.

## Decision

The board grid/matrix and explicit piece state own gameplay truth.

* Placement, occupancy, gravity, splitting, and shredder reservation use authoritative grid coordinates and state transitions.
* Colliders are physics adapters where approved; they do not replace the board model.
* Renderers, overlays, materials, masks, VFX, and tweens observe state and may change without mutating board truth.
* Presentation fixes must preserve collider geometry and board occupancy unless the task explicitly changes gameplay.

## Consequences

* A missing or stale sprite is fixed inside presentation ownership rather than by changing grid data.
* Hammer fragments can replace their visual skin while keeping the same retained cells and collision geometry.
* Obstacle visuals may be merged or sliced independently of their collision objects.
* Pool reset must clear presentation state completely so a later renter cannot inherit renderer ownership from a previous piece.
