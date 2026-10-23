# ADR-003: Tunnel Content Activation

## Status

Accepted.

## Context

Tunnel contents are authored as `PieceDefinition` values under a `BoxRevealDefinition` (the existing serialized name). Before this decision, a Box rendered those definitions as presentation-only previews and rented all real `BlockPiece` roots only after its final opening animation completed. That made a visually exposed lower-row block unavailable to normal board gravity.

The board grid remains the single authority for occupancy, movement, gravity, shredding, and collision legality. A shutter image, preview renderer, collider, or world transform cannot independently authorize gameplay.

## Decision

The `RevealAreaCoordinator` owns one runtime content record for every hidden Box definition. The record is keyed by its authored list index and is the sole source of whether that definition has rented a real gameplay root.

`BoxRevealAreaView` reports a monotonic, normalized shutter exposure value through an explicit presentation-to-gameplay boundary. The coordinator compares that value with the complete authored footprint of each unactivated definition. It may activate a definition only after every block cell is exposed. A multi-cell definition is never split. The board performs the same atomic pooled-root, grid-placement, model-registration transaction used by final reveal activation.

An activation record is marked active only after that transaction succeeds. Final rupture activates only records still inactive. This makes an early-activated or moved root the same object that survives final unlock; no second root can be created for it.

The normal Tunnel preview remains the recessed representation of an activated item while that item is inside a partially open Tunnel. Its real root exists and owns grid occupancy, but its renderers are hidden. Exposure releases that root only to the normal grid-gravity planner; it never grants player input while the root remains inside the Tunnel. A legal downward gravity path commits the root out of the Tunnel, where it permanently resumes normal rendering and gameplay. Pool despawn always clears this temporary presentation lease.

While a Tunnel is not fully unlocked:

* no content inside the Tunnel can begin a drag;
* an activated content piece participates in gravity only when its complete footprint is exposed; its legal path may leave the Tunnel but cannot enter a shutter-covered cell or re-enter after leaving;
* an activated content blocked below remains recessed, hidden, and non-draggable inside the Tunnel;
* unrelated pieces cannot enter any portion of the Tunnel; it remains a virtual solid obstacle;
* Hammer and Rocket targeting reject every content record belonging to the still-partial Tunnel.

After full unlock, all remaining definitions activate and every Box-specific movement, gravity, targetability, and presentation restriction is released.

## State transitions

| Event | Content record | Gameplay root | Preview / rendering | Movement and gravity |
| --- | --- | --- | --- | --- |
| Tunnel locked | Inactive | Absent | Recessed preview, shutter-covered | No interaction; Tunnel is a virtual obstacle |
| Shutter fully exposes a definition | Activated inside Tunnel | Rented, placed, registered, `Placed` | Preview remains recessed; real renderers hidden | Normal grid gravity may plan only a legal downward exit; input remains disabled |
| Gravity blocked below | Activated inside Tunnel | Same root, `Placed` | Real root hidden; matching preview remains | Non-draggable and held by the grid support |
| Gravity commits an exit | Activated outside Tunnel | Same root | Normal root presentation permanently restored | Normal gravity and input apply; no re-entry before full unlock |
| Shutter advances | Unchanged | Same root | Other previews update normally | No lifecycle reset or duplicate activation |
| Final rupture | Remaining inactive records activate | Existing roots preserved | Final callback clears Tunnel presentation leases | All Tunnel restrictions release after final activation |

## Consequences

The shutter exposure callback is intentional and documented: it communicates a monotonic visual aperture value, while the coordinator owns all activation and gravity-rule mutations. No gameplay rule reads renderer bounds, colliders, or transforms as authority.

Tunnel virtual-obstacle checks must distinguish an activated content model from unrelated models. Gravity planning continues to use the same per-candidate movement predicate, preserving the closed-Tunnel deadlock and tunnelling protections. Elevator virtual occupancy must count only not-yet-activated Tunnel definitions; activated roots are already visible in the board snapshot and must not be mirrored as stale authored occupants.
