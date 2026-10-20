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

Bomb and ice overlays have separate ownership and cleanup. Ice refresh and release
must not fade or clear the bomb icon or its counter; bomb cleanup is performed
explicitly on presentation rebuild, defuse/fragment handling, and pool reset.

Presentation slots are pooled state. Every renter must receive reset sprite, material, colour, transform, sorting, mask, enabled state, and property-block state.

Normal brick colours use the named Scene Tuna material palette in `PieceVisualConfig`.
Level pieces own their semantic palette ID; the material owns the rendered colour,
so normal renderers use white tint. The level editor assigns a palette when a
piece is added or its authoring colour changes and exposes the ID for deliberate
overrides. Legacy assets can be assigned via the non-destructive palette migration
menu, which preserves existing IDs. The fallback material remains for missing or
unsupported palette materials, not as the intended normal appearance.

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
* Box reveal presentation uses a fixed nine-sliced frame, an opaque rectangular backing, and a clipped shutter behind the frame. The frame and shutter textures are derived from the `Tunnel` artwork in `map.png`; the white rim and bottom edge remain on the frame. The frame sprite imports at 145 pixels per unit to keep its nine-sliced border thinner than the source atlas; opening inset conversion uses that same pixels-per-unit value. Adjust `Box Frame Thickness` on `Assets/Config/RevealPresentationConfig.asset` to scale the frame borders and opening together; lower values produce thinner borders. The overall box footprint stays fixed and the value takes effect when the level is rebuilt or reloaded. The aperture is the rounded rectangle from pixel (31, 56) to (426, 418), measured from the top-left of that 458 × 467 source sprite; the shutter is cropped from that region. Preserve the unmodified atlas and keep the layer artwork and insets synchronized when editing it. The shutter material clips and translates its UVs within its own aperture, so other boxes and shredder masks do not share its mask. `RevealPresentationConfig.boxOpeningInsets` records the aperture edges in source pixels and must be updated if the frame artwork changes. The prefab and `SceneTunaVisualAuthoring` must both retain all three renderers.

  Before opening, a Box also prepares presentation-only previews of its hidden pieces. `RuntimePieceFactory.AppendPreviewVisuals` resolves the same base piece sprites, palette materials, whole-shape visuals, placement, and scale used by the eventual pooled pieces; it does not rent gameplay roots or create colliders. The pre-authored `Preview Content` slots in `BoxRevealArea.prefab` render between the cavity backing and shutter, and their `SpriteMask` clips them to the aperture. Their recess distance, brightness, and scale ease to the real piece appearance from the same shutter opening value. Previews remain visible through the frame burst and are reset only when the pooled Box returns immediately before the unchanged reveal callback activates the actual pieces. A Box with an empty `hiddenPieces` list is intentionally an empty cavity; use **Capture Fully Covered Board Pieces** in the level editor to migrate whole pieces inside that Box into its hidden content. `Box Preview Slot Capacity` belongs to `RevealPresentationConfig`; after changing it, run `Gravity Puzzle/Art/Refresh Box Reveal Preview Slots` to resize the pre-authored pool. A slot-capacity warning means a Box has more visual parts than its prefab was authored to show; gameplay activation remains unaffected. The coordinator supplies the original target, remaining count, and hidden-piece data; it remains the sole owner of unlock timing and board activation.

  After the shutter reaches the top, the frame briefly recoils and fades as its 12 pre-authored fragments sample the original blue frame artwork through `BoxFragment.mat`, scatter from the frame perimeter, rise, fall and fade. `boxFragment*Region` values in the reveal config describe normalized subrectangles of the original Tunnel sprite; keep them aligned with that sprite if its artwork changes. The box view owns the whole linked sequence and kills it on pool return; no shredder particle system participates. The opaque backing stays through the final burst and is removed when the view returns to its pool, before the completion callback activates hidden pieces. Hidden pieces and collision remain governed by the reveal lifecycle, not the visible aperture. The dedicated TextMeshPro counter renders at sorting order 201 by default.
* Elevator mechanics do not have a countdown state in the gameplay contract; elevators feature animated sliding doors rather than a number counter.

## Joint Springs

* Piece joints are visualized using `JointSpringView` pooled instances managed by `JointSpringPresenter`.
* On shredder handoff the presenter transfers affected spring views to the feeding piece before the gameplay joints are removed. The existing feed tick releases each view near the cutter; it kicks diagonally upward and sideways, rotates and then fades independently before returning to its pool. Direction and intensity vary deterministically by spring key, and the world-space flick preserves the initial orientation of either seam. Feed cleanup returns any remaining views, and reconfiguration returns both feeding and animating views. ShredderConfig owns the visual release tuning; the effect does not delay or change joint or piece state.
* Springs observe `PieceJointGraph.JointsChanged` events and are parented to an endpoint piece (`anchorPiece`), so they follow normal board movement, dragging, and gravity falls without per-frame polling. They never dictate board connections or physics handoff.
* `JointAnchorConfig` stores sprite-relative normalized stud centres and the two spring-end centres. The presenter transforms authored studs through the active simple sprite renderer (including flips, scale and rotation), selects the pair near each grid-owned joint, and fits the spring's two ends to them. The grid remains the joint authority. Tiled sprites and sprites without complete authored data retain the legacy placement; layouts are not assumed interchangeable between different artwork.
* The 28 `BrickBlocks.png` atlas sprites have stud layouts measured from their artwork; the original manually placed 1x1 markers are retained. Other atlases and newly added sprites require their own layouts and must not reuse these coordinates without verification.
* To author, select `Assets/Config/Presentation/JointAnchorConfig.asset`. Add or select a sprite layout and click each stud in the zoomable preview (right-click removes a nearby marker). Select Edit Spring Ends, click end A, Shift-click end B, then enable Spring Ends Authored. Coordinates are normalized from zero to one, not pixels; out-of-range data triggers a warning and legacy placement. Reload the level after editing. Scene1's presenter references this shared asset; other gameplay scenes with their own presenter must reference it too. Only add a sprite layout after checking that sprite's actual stud centres. Enable Log Anchor Diagnostics on the presenter to log the chosen path when joints reconcile.
* For a separate visual reference, use `Gravity Puzzle > Presentation > Create Spring Calibration Example` in Edit Mode. It creates an EditorOnly scene object away from the board with two example blocks and a prefab instance named `Adjust This Spring`; edit that child's transform in the Inspector. The example uses the first authored block sprite and approximates one-unit block bounds. It is a manual reference only: its transform does not alter the presenter or automatically calibrate gameplay. Save the scene to retain it; undo the creation to remove it.
* The optional reference-fit points are distinct from the visible black spring ends. The current values are an average derived from the two adjusted Scene1 example springs and the authored 1x1 stud points; this transfers the example's approximate size and offset without changing its marked black ends. They are static configuration, not a live connection to the example. Changing that example later requires recalibration and visual verification.
* The legacy fallback uses two springs per cell seam with `±0.25` spacing and the presenter's scale and hole offset; these are not evidence of pixel-perfect alignment.
* **Inspector Alignment & Tuning**: `JointSpringPresenter` owns the legacy tuning fields and orientation nudges. Its `OnValidate()` reconciles active views in Play Mode. The similarly named `PieceVisualConfig` fields are not currently consumed by this presenter.
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


