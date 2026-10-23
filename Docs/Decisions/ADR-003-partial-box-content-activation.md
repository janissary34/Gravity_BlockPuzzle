# ADR-003: Partial Box Content Activation

## Status

Accepted.

## Context

Box contents are authored as `PieceDefinition` values under a `BoxRevealDefinition`. Before this decision, a Box rendered those definitions as presentation-only previews and rented all real `BlockPiece` roots only after its final opening animation completed. That made a visually exposed lower-row block unavailable to normal board input.

The board grid remains the single authority for occupancy, movement, gravity, shredding, and collision legality. A shutter image, preview renderer, collider, or world transform cannot independently authorize gameplay.

## Decision

The `RevealAreaCoordinator` owns one runtime content record for every hidden Box definition. The record is keyed by its authored list index and is the sole source of whether that definition has rented a real gameplay root.

`BoxRevealAreaView` reports a monotonic, normalized shutter exposure value through an explicit presentation-to-gameplay boundary. The coordinator compares that value with the complete authored footprint of each unactivated definition. It may activate a definition only after every block cell is exposed. A multi-cell definition is never split. The board performs the same atomic pooled-root, grid-placement, model-registration transaction used by final reveal activation.

An activation record is marked active only after that transaction succeeds. Final rupture activates only records still inactive. This makes an early-activated or moved root the same object that survives final unlock; no second root can be created for it.

The normal Box preview remains the recessed representation of an activated item while that item is idle inside a partially open Box. Its real root exists and owns grid occupancy, but its renderers are hidden. On an accepted drag start, the coordinator hides the matching preview and exposes the already-registered root with its original colours, brightness, and shared materials, rendered above the Box shutter and frame without a SpriteMask. If a release or cancellation leaves the root inside the Box, the root is hidden again and the preview resumes. Once the root has left the Box, it permanently resumes normal rendering. Pool despawn always clears this temporary presentation lease.

While a Box is not fully unlocked:

* only its activated contents can begin a drag;
* an activated content piece may traverse exposed cells and leave the Box, but cannot enter a shutter-covered cell or re-enter after leaving;
* activated content remaining inside the Box is ineligible for gravity;
* unrelated pieces cannot enter any portion of the Box; it remains a virtual solid obstacle;
* Hammer and Rocket targeting reject every content record belonging to the still-partial Box.

After full unlock, all remaining definitions activate and every Box-specific movement, gravity, targetability, and presentation restriction is released.

## State transitions

| Event | Content record | Gameplay root | Preview / rendering | Movement and gravity |
| --- | --- | --- | --- | --- |
| Box locked | Inactive | Absent | Recessed preview, shutter-covered | No interaction; Box is a virtual obstacle |
| Shutter fully exposes a definition | Activated inside Box | Rented, placed, registered, `Placed` | Preview remains recessed; real renderers hidden | Drag allowed; gravity held; only exposed traversal/exit allowed |
| Accepted drag start | Activated inside Box | Same root, `Dragging` | Matching preview hidden; real root uses normal appearance above shutter/frame, unmasked | Existing continuous drag and grid validation apply |
| Invalid release or cancellation inside Box | Activated inside Box | Same root restored to `Placed` at last legal anchor | Real root hidden; matching preview restored | Still draggable; gravity remains held |
| Valid release inside Box | Activated inside Box | Same root, `Placed` | Real root hidden; matching preview restored | Still draggable; gravity remains held |
| Valid traversal outside Box | Activated outside Box | Same root | Normal root presentation permanently restored | Normal gravity applies; no re-entry before full unlock |
| Shutter advances during drag | Unchanged | Same root | Drag root remains foreground; other previews update normally | No lifecycle reset or duplicate activation |
| Final rupture | Remaining inactive records activate | Existing roots preserved | Existing drag root remains foreground through rupture; final callback clears Box presentation leases | All Box restrictions release after final activation |

## Consequences

The shutter exposure callback is intentional and documented: it communicates a monotonic visual aperture value, while the coordinator owns all activation and rule mutations. No gameplay rule reads renderer bounds, colliders, or transforms as authority.

Box virtual-obstacle checks must distinguish an activated content model from unrelated models. Gravity planning continues to use the same per-candidate movement predicate, preserving the closed-box deadlock and tunnelling protections. Elevator virtual occupancy must count only not-yet-activated Box definitions; activated roots are already visible in the board snapshot and must not be mirrored as stale authored occupants.
