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

* Board frames use artist-authored directional atlas sprites (`Edge_Left`, `Edge_Right`, `Edge_Top`, and corners) from `map.png` / `BoardPresentationConfig`.
* In `Scene_Tuna` and `Scene1`, the full modular border hierarchy (`GameObject (5)`) is scene-authored with 35 distinct 1x1 block obstacle-like modules (12 left, 12 right, 6 top, and corners) rather than flat stretched tubes.
* `BoardPresentationConfig.UseSceneAuthoredBorders` configures `GravityLevelRuntime.CreateBoardFrame` to suppress procedural frame runner generation when scene-authored borders are active, while continuing to generate exact physics collision bounds via `CreateMapCollisionBlocks`.
* For dynamic procedural levels without scene-authored borders, `TryCreateRepeatedFrameModules` generates repeated 1x1 modular block tiles along the edges, preserving the 3D beveled block / obstacle aesthetic across all level dimensions.

## Counters and Piece Overlays

* Ice counters and Bomb counters have separate presentation ownership. `BlockPiece` provides dedicated TextMeshPro child slots (`Ice Counter Presentation` and `Bomb Counter Presentation`) so that active bomb pieces do not mutate or overwrite ice counter styling.
* Ice counters and bomb counters derive their world-space font sizes from `PieceVisualConfig` (defaults 0.55 and 0.65 respectively) scaled proportionally by level-definition font size properties (where 36 represents standard 1.0x scale).
* Ice locks player input and shredder capture until its counter is cleared, but does not make a piece a gravity support. A frozen piece, including a member of a connected group, still follows a legal grid fall.
* Box reveal covers use the `Tunnel` sprite from `map.png` with 9-slice border configuration, accompanied by a dedicated TextMeshPro counter rendered at sorting order 201 with navy outline.
* Elevator mechanics do not have a countdown state in the gameplay contract; elevators feature animated sliding doors rather than a number counter.

## Joint Springs

* Piece joints are visualized using `JointSpringView` pooled instances managed by `JointSpringPresenter`.
* Springs observe `PieceJointGraph.JointsChanged` events and are parented to an endpoint piece (`anchorPiece`), so they follow normal board movement, dragging, and gravity falls without per-frame polling. They never dictate board connections or physics handoff.
* Connecting springs are oriented horizontally across vertical boundaries and vertically across horizontal boundaries, positioned exactly at opposing stud centers (one spring per connected stud pair, spaced at `±0.25f` along the seam).
* The spiral sprite scale is calibrated to `0.1495036f` to match the exact 0.50 world-unit stud distance between 2x2 modular block studs, with a vertical hole offset of `0.099f` ensuring the spiral holes sit dead-center on top of the studs at sorting order 20.
* **Inspector Alignment & Tuning**: `JointSpringPresenter` (and `PieceVisualConfig`) exposes serialized tuning fields (`springScale`, `studOffset`, `holeDepthOffset`, `positionOffset`, `horizontalJointOffset`, `verticalJointOffset`). In Unity Editor Play Mode, modifying any of these fields triggers `OnValidate()` to reconcile active views in real time for immediate visual alignment feedback.

