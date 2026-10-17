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
  Cached full-rectangle sprites retain the source atlas's secondary textures, including `_ShineMask`, so the shared obstacle shader can use the same UV-aligned highlight data as imported sprites. The sliced sprite uses the one-cell source's pixel height as pixels per unit: its border then has the same world-space proportions as the source fitted to one grid cell. Switching the renderer to sliced mode must precede restoring unit transform scale. These adaptations do not change the atlas import settings for unrelated artwork.

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

The decorative frame uses the Scene Tuna atlas pieces `Corner_Top_Left`, `Corner_Top_Right`, `Edge_Top`, `Edge_Left`, and `Edge_Right`. Scene Tuna's grid cells are 0.77 world units wide; its authored 0.5 sprite scale is therefore converted to `0.5 / 0.77` for the one-unit runtime grid. The top strip retains the source's vertical flip. Source-relative join positions live in `BoardPresentationConfig`; no generated backdrop or replacement frame art participates in this frame.

`Assets/Prefabs/SceneTunaBoardFrame.prefab` provides pre-authored pools under `LevelRuntimeRoot/Composite Board Frame`. For open-bottom boards, `GravityLevelRuntime` places the two top corners first and fills the outer bounds with matching source strips; the last full-size strip overlaps to fit the remainder rather than stretching its artwork. The first SpriteRenderer in each active role's Slots group is its editable template: its sprite, material, color, sorting order and flip are copied to that role's placed modules. Edit those templates in the frame prefab; module positions and scales are controlled by level geometry and `BoardPresentationConfig`. A rented slot's GameObject is activated as well as its renderer, because copied Scene Tuna objects may have been authored inactive. These modules are presentation-only; `CreateMapCollisionBlocks` remains the sole source of collision and grid occupancy.

Run `Gravity Puzzle/Level Authoring/Rebuild Level Runtime Root` after changing module capacity. It validates configured levels and leaves the first valid level as an edit-mode preview. The pre-authored objects remain disabled until a preview/build configures them; this avoids runtime `Instantiate`/`Destroy`.

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
* **Lifecycle & Teardown**: In-scene reconfigurations (`Configure`) return active springs via `ClearAll()`. Teardown (`OnDestroy`) unbinds graph events, releases board references, and clears tracked dictionaries without attempting to reparent active views to a destroying pool parent.

## Shredder Feed Presentation

* **Occlusion & Masking**: Pieces entering the shredder are visually clipped using a pooled `ShredderFeedMask` whose top edge is inset into the shredder mouth (`FeedMaskMouthInset`).
* **Visual Layering**:
  * Puzzle Pieces: sorting orders 5..9
  * Shredder Wheels (disc, teeth, hub): sorting orders 25..27
  * Contact Debris Particles: sorting order 28
  * Friction Contact Glow: sorting order 29
* **Additive Friction Contact Glow**:
  * Handled by pooled `ShredderContactGlowView` (`ShredderContactGlowPool`), prewarmed at bootstrap.
  * Uses an additive Universal2D shader (`ShredderContactGlow.shader`, `Blend One One`) and high-frequency friction pulsation to blow out pixel contrast right at the contact seam, disguising the horizontal clipping boundary.
  * Rented once per piece feed coroutine and tracked against `TryGetCutterContactSurface` bounds without per-frame garbage allocations, then safely returned in the feed coroutine's `finally` block.


