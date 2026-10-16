# Presentation Invariants

## Piece renderer ownership

`PuzzlePiece` keeps authored presentation renderers in `Ice Presentation Slots` on the pooled `BlockPiece` prefab.

The first renderer in this collection is reserved for the intact whole-piece silhouette (`wholePieceRenderer`). It must not be borrowed by bomb, ice, frost, release, or other overlay effects.

Overlay allocation must use the existing available-slot selection path so it excludes:

* `wholePieceRenderer`;
* renderers already owned by an active overlay;
* and renderers currently composing a joined piece presentation.

Bomb overlays and ice layers both use `TryGetAvailableIceSlot`. Do not restore
direct collection indexing: slot zero may be the whole-piece renderer even when
the active overlay list is empty.

Presentation slots are pooled state. Every renter must receive reset sprite, material, colour, transform, sorting, mask, enabled state, and property-block state.

## Whole-piece and modular fallback selection

Gameplay colliders and board cells remain unchanged when presentation switches between these paths:

1. Use an exact artist-authored atlas silhouette when the normalized module shape has a configured match. Straight `3x1` and `4x1` obstacle runs use their matching `map.png` sprites instead of sliced fallback rectangles.
2. Otherwise compose contiguous horizontal and vertical runs from joined fallback sprites.
3. Use individual 1x1 modules only for cells not covered by an exact or joined presentation.

Hammer fragments must rebuild presentation from their retained authoritative cells. A two-cell horizontal or vertical fragment must use the exact `2x1` or `1x2` atlas sprite when available; it must not resize a `3x1` or `1x3` texture into two cells because that compresses the stud count and lighting.

## Obstacles

Obstacle gameplay collision and obstacle visuals have separate ownership:

* `Obstacle N Collision` objects provide static collision.
* A matching atlas silhouette is rendered as `Merged Obstacle Visual`.
* Shapes without a matching silhouette are decomposed into `Sliced Obstacle Visual` rectangles and join visuals.

Visual merging must never change authored obstacle cells, collider dimensions, or board occupancy.

The outer bounds of a merged or sliced obstacle visual must match its authored
cell footprint. Presentation overlap is allowed only for internal seam coverage;
it must not extend a sliced rectangle into a neighbouring playable cell.

## Straight-piece asset authoring

Generated straight-piece sprites are presentation silhouettes, so their canvas
and visible alpha bounds must describe the same logical shape. A `1x3` sprite,
for example, uses a `1:3` canvas and must not contain distant low-alpha pixels or
large transparent end gutters. Runtime fitting intentionally uses the imported
sprite bounds; it does not compensate for malformed source-image bounds.

## Presentation refreshes

A presentation refresh may rewrite sprites, materials, visibility, sorting, and overlay state. It must not repair gameplay state as a side effect. If a visual appears only after another piece is destroyed or shredded, treat that as evidence of stale or conflicting presentation ownership rather than relying on the later refresh.

## Directional Board Frame

Board frames use 8 artist-authored directional atlas sprites (4 corners and 4 edges) from `BoardPresentationConfig`. The old solid quad fallback panel is suppressed when authored frame sprites are available. Edges are composed of repeated 1x1 modules to maintain consistent stud scale and visual density without stretching.

## Counters and Piece Overlays

* Ice counters and Bomb counters have separate presentation ownership. `BlockPiece` provides dedicated TextMeshPro child slots (`Ice Counter Presentation` and `Bomb Counter Presentation`) so that active bomb pieces do not mutate or overwrite ice counter styling.
* Box reveal covers use a dedicated TextMeshPro counter rendered at sorting order 201 with navy outline.
* Elevator mechanics do not have a countdown state in the gameplay contract; elevators feature animated sliding doors rather than a number counter.

## Joint Springs

* Piece joints are visualized using `JointSpringView` pooled instances managed by `JointSpringPresenter`.
* Springs strictly observe `PieceJointGraph.JointsChanged` events and never dictate board connections or physics handoff.
* Connecting springs are oriented horizontally across vertical boundaries and vertically across horizontal boundaries, positioned exactly at opposing stud centers (one spring per connected stud pair).

