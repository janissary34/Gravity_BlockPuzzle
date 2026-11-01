# Presentation Invariants

## Booster targeting dimmer

The world-space board dimmer is presentation-only. While Hammer or Rocket
targeting is armed, every active board piece temporarily leases foreground
sorting above that dimmer; this keeps pieces readable without making their
render order gameplay authority. The lease captures and restores sprite and
world-space text sorting on cancellation, target confirmation, pooling, and
runtime reconfiguration. A first-use tutorial can replace that broad lease
with its single explicit target lease.

When a first-use booster prompt raises that dimmer, the authored feature panel
is visible as well. Its copy identifies the Rocket, Hammer, or Freeze Timer
action; it does not own targeting validation or booster state.

## Freeze Timer button ownership

`Time_Booster_Button` owns the Freeze Timer presentation, its scene-authored
timer references, and the authoritative freeze activation. Its
`Time_Booster_Button(Enable)` and `(Disable)` children are visual button-state
variants. The enabled child keeps the raycast Button but forwards activation to
the parent `TimerBooster` through its explicit `presentationSequenceOwner`
reference; it must not own a duplicate timer presentation setup. This preserves
one timer-sequence owner while allowing authored enabled/disabled visuals.

## New Booster reward visuals

`NewBoosterPanelView` owns which pre-authored reward slot is visible. Each
`BoosterRewardContentView` owns the active state of its child
`BoosterVisualView` roots: opening a selected reward re-enables its icon and
hiding the slot disables it. This prevents a Rocket, Hammer, or Timer visual
from inheriting an inactive state from an earlier reward presentation. These
objects remain presentation-only and are never runtime-instantiated.

## Modal action buttons

`PanelEntrancePresentation` can list direct children in `Center Scale Excluded
Content`. Those controls keep their authored scale while the modal enters. The
Fullscreen Success Claim and x2 action buttons use this path so modal entry
does not look like a button press or collapse their button artwork.

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
explicitly on presentation rebuild, Hammer defuse, destruction, and pool reset.
A Hammer hit on an armed bomb removes only its icon and countdown, preserving every
piece cell, joint, and authoritative grid footprint. After defusal, the piece is
handled as a normal block by later Hammer hits and by the shredder.
While Hammer targeting is active, normal and defused pieces use the pooled per-cell
outline. An armed bomb instead uses its existing composite whole-piece outline, so
the target feedback matches Hammer's one-action bomb defuse behavior.
The Hammer's visual strike is centered on the armed Bomb icon while gameplay still
validates the selected grid cell. At impact, two temporary pooled presentation slots
render the left and right icon halves with the shared Bomb split material, separate
briefly, then fade. This is presentation-only; the bomb is defused immediately and
its counter is removed without changing the piece's grid footprint.
An armed Bomb icon is also appended to the shredder presentation renderer set. It
therefore uses the same cutter dissolve and clipping path as its block; its counter
is hidden only when the icon has fully crossed the cutter line.

Presentation slots are pooled state. Every renter must receive reset sprite, material, colour, transform, sorting, mask, enabled state, and property-block state.

Normal brick colours use the named Scene Tuna material palette in `PieceVisualConfig`.
Level pieces own their semantic palette ID; the material owns the rendered colour,
so normal renderers use white tint. The level editor assigns a palette when a
piece is added or its authoring colour changes and exposes the ID for deliberate
overrides. Legacy assets can be assigned via the non-destructive palette migration
menu, which preserves existing IDs. The fallback material remains for missing or
unsupported palette materials, not as the intended normal appearance.

Piece selection glow is presentation-only: it neither changes collision nor board
occupancy. `RuntimePieceFactory` derives its contour solely from the piece's retained
fine-grid occupancy. It removes shared cardinal edges, traces the remaining closed
boundary loops, and emits explicit fixed-segment arcs at convex corners. Concave
corners use intersecting offset edges. Sprite alpha, Sprite physics shapes,
`CompositeCollider2D` paths, and collider skin are never read for this presentation.

`BlockPiece.prefab` owns one root outline renderer and seven disabled child renderers
named `Outline Loop 1` through `Outline Loop 7`. `PuzzlePiece` caches that fixed
pool once in `Awake`; every loop is independently configured, reset, hidden for Box
reveal and shredder presentation, and restored with the same colour, width, and
sorting state. The eight-renderer capacity is an authored presentation limit: if a
future piece needs more loops, increase the prefab slot count deliberately rather
than creating a renderer at runtime.

`PieceVisualConfig` owns line width, visual inset, outer padding, corner radius, and
corner-segment count. The selected-line half width and padding move the stroke out;
the atlas visual inset moves its centre back only enough for the inner edge to meet
the visible block art. Convex and concave grid corners both receive explicit arcs,
so an L-notch is not left as a sharp offset-line intersection. Freeze/thaw only
reapplies visual state; spawning and fragment rebuilds regenerate the grid outline.
Selecting a piece in the Unity editor draws every generated loop's points and
segments as magenta Gizmos.

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

## Build verification level advance

`InGameUI` contains the authored `Debug_Next_Level_Button` for device-build verification. `DebugNextLevelButton` is an input adapter only: it delegates to `PrototypeBoard.TryAdvanceToNextLevelForDebug`, which advances the existing `GravityLevelRuntime` campaign sequence and reloads the active scene through `SplashSceneLoader`. It intentionally does not clear the board, grant completion rewards, queue booster unlocks, or persist tutorial progress. The control is disabled on the final configured level.

## Directional Board Frame

The decorative frame uses the Scene Tuna atlas pieces `Corner_Top_Left`, `Corner_Top_Right`, `Edge_Top`, `Edge_Left`, and `Edge_Right`. Scene Tuna's grid cells are 0.77 world units wide; its authored 0.5 sprite scale is therefore converted to `0.5 / 0.77` for the one-unit runtime grid. The top strip retains the source's vertical flip. Source-relative join positions live in `BoardPresentationConfig`; no generated backdrop or replacement frame art participates in this frame.

`Assets/Prefabs/SceneTunaBoardFrame.prefab` provides pre-authored pools under `LevelRuntimeRoot/Composite Board Frame`. For open-bottom boards, `GravityLevelRuntime` places the two top corners first and fills the outer bounds with matching source strips; the last full-size strip overlaps to fit the remainder rather than stretching its artwork. The first SpriteRenderer in each active role's Slots group is its editable template: its sprite, material, color, sorting order and flip are copied to that role's placed modules. Edit those templates in the frame prefab; module positions and scales are controlled by level geometry and `BoardPresentationConfig`. A rented slot's GameObject is activated as well as its renderer, because copied Scene Tuna objects may have been authored inactive. These modules are presentation-only; `CreateMapCollisionBlocks` remains the sole source of collision and grid occupancy.

Run `Gravity Puzzle/Level Authoring/Rebuild Level Runtime Root` after changing module capacity. It validates configured levels and leaves the first valid level as an edit-mode preview. The pre-authored objects remain disabled until a preview/build configures them; this avoids runtime `Instantiate`/`Destroy`.

## Counters and Piece Overlays

* Ice counters and Bomb counters have separate presentation ownership. `BlockPiece` provides dedicated TextMeshPro child slots (`Ice Counter Presentation` and `Bomb Counter Presentation`) so that active bomb pieces do not mutate or overwrite ice counter styling.
* Ice counters derive their world-space font size from `PieceVisualConfig` (default 0.55) scaled proportionally by their level-definition font-size property (where 36 represents standard 1.0x scale). Bomb counter font size is global: `PieceVisualConfig.BombCounterFontSize` (default 0.65) is the only runtime or level-editor control. A bomb overlay uses a 0.936 fill and a one-authored-block fit target—not the complete 1-, 2-, 3-, or 4-block footprint. It uses the shape center only when that one-block footprint is fully covered; concave shapes, such as L pieces, instead use the occupied block closest to the area-weighted shape center. Scene Tuna's raw font-size value uses that scene's independent scale and must not be copied directly into the runtime configuration.
* `PrototypeBoard` owns both the level and armed-bomb countdowns. Its owner-scoped timer pause freezes both timers, including the Freeze Timer booster's presentation lead-in and active duration; bomb counters therefore update only while that board timer is running.
* Ice locks player input, gravity movement, and shredder capture until its counter is cleared. A frozen piece remains fixed in place, acts as a solid gravity support for pieces resting on it, and does not fall until its countdown reaches zero.
* Tunnel reveal presentation has two independent visual layers. The sole `Tunnel Frame` renderer uses the open-bottom `Tunnel` sprite from `map.png`, remains static above all other Tunnel layers, and is the only visible frame. That atlas imports with a Full Rect mesh because this sprite is 9-sliced; do not return it to Tight mesh, which makes Unity tile the U frame unreliably. The backing, moving `TunnelShutter`, and preview mask are sized directly from the reveal-area size using `boxPanelSizeMultiplier` and `boxPanelOffset`; they must never be calculated from the U frame's 9-slice border. `BoxShutter.shader` samples `boxPanelSourceUvRect`, which excludes the rounded perimeter baked into `TunnelShutter.png`, so only its slatted interior is visible and it cannot create a nested frame or dark lower corners. The preview `SpriteMask` uses the opaque rectangular backing sprite rather than the rounded shutter sprite. Preserve the unmodified source art and update `SceneTunaVisualAuthoring` with this composition whenever it changes: regeneration must produce one frame renderer, the backing, the shutter, and the rectangular preview mask—never a flipped bottom-frame copy. `Box Frame Thickness` affects only the U-frame rendering; the panel fields independently control the inner presentation at every Tunnel size.

  Before opening, a Box also prepares presentation-only previews of its hidden pieces. `RuntimePieceFactory.AppendPreviewVisuals` resolves the same base piece sprites, palette materials, whole-shape visuals, placement, and scale used by the eventual pooled pieces; it does not rent gameplay roots or create colliders. The pre-authored `Preview Content` slots in `BoxRevealArea.prefab` include matched, tinted shadow slots behind every preview and a `SpriteMask` that clips both layers to the aperture. Their recess distance, brightness, and scale ease to the real piece appearance from the same shutter opening value. The shadow uses the same sprite and material as its preview, offset and expanded slightly through config; it fades with the cavity in the final shutter movement. At rupture, the backing and shadows are removed and the aperture mask is disabled, allowing the preview pieces—including their edge pixels—to remain visible until the unchanged reveal callback activates the real pieces. A Box with an empty `hiddenPieces` list is intentionally an empty cavity. Locking a Box in the level editor automatically migrates every whole board piece fully inside its bounds into that list; unlocking restores those pieces to the editable board and clears the Box contents. **Capture Fully Covered Board Pieces** remains available to capture additional complete pieces after authoring changes. `Box Preview Slot Capacity` belongs to `RevealPresentationConfig`; after changing it, run `Gravity Puzzle/Art/Refresh Box Reveal Preview Slots` to resize the paired pre-authored pool. A slot-capacity warning means a Box has more visual parts than its prefab was authored to show; gameplay activation remains unaffected. The coordinator supplies the original target, remaining count, and hidden-piece data; it remains the sole owner of unlock timing and board activation.

  A partially opened Tunnel can activate a complete hidden definition once the shutter has exposed its entire footprint. While it remains inside, the activated root stays renderer-hidden and its original recessed preview continues to represent it; it cannot be dragged or booster-targeted. The coordinator releases that root to normal grid gravity immediately. If the authoritative grid has a clear downward exit, the fall commits, the root leaves the Tunnel, and its normal rendering and gameplay resume permanently. If the exit is blocked, the root remains hidden and non-interactive until gravity can move it. This is a per-instance presentation lease, never a shared-material mutation. See `Decisions/ADR-003-partial-box-content-activation.md` for the gameplay transition contract.

  After the shutter reaches the top, the frame briefly recoils and fades as its 12 pre-authored fragments sample the original blue frame artwork through `BoxFragment.mat`, scatter from the frame perimeter, rise, fall and fade. The ten top/side edge fragments are scaled slightly larger and hold at the aperture for the configured initial portion of their lifetime before scattering. `boxFragment*Region` values in the reveal config describe normalized subrectangles of the original Tunnel sprite; keep them aligned with that sprite if its artwork changes. The cavity backing and preview shadows fade during the final `boxBackingFadeStart` portion, and are disabled together with the aperture mask at rupture; preview-piece borders must therefore never be replaced by a dark cavity edge. The box view owns the whole linked sequence and kills it on pool return; no shredder particle system participates. Hidden pieces and collision remain governed by the reveal lifecycle, not the visible aperture. The dedicated TextMeshPro counter renders at sorting order 201 by default.
* Elevator mechanics do not have a countdown state in the gameplay contract; elevators feature animated doors rather than a number counter. Generated views use `Elevator` and `Elevator_Door` from `map.png`; `RevealPresentationConfig` owns these references. Both door renderers use the same panel sprite and only the left panel is mirrored. The left panel's outer-left edge and the right panel's outer-right edge remain fixed; their inner edges collapse horizontally into those fixed edges. The view then disables every Elevator visual layer and returns its root to the pool, so no source-frame artwork can remain over the board grid. This is presentation-only; it does not control when fixed interior content activates.

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
* **Progress Grain Budget**: `ShredderConfig.ProgressVoxelMultiplier` is the direct number of Canvas grains per authored board block. It must not be combined with an implicit per-block multiplier. `TweenConfig.ProgressVoxelUiBurstCount` is only the renderer's per-burst safety cap; it does not define the shredder reward value.
* **Visual Layering**:
  * Puzzle Pieces: sorting orders 5..9
  * Shredder Wheels (disc, teeth, hub): sorting orders 25..27
  * Contact Debris Particles: sorting order 28
  * Friction Contact Glow: sorting order 29
* **Additive Friction Contact Glow**:
  * Handled by pooled `ShredderContactGlowView` (`ShredderContactGlowPool`), prewarmed at bootstrap.
  * Uses an additive Universal2D shader (`ShredderContactGlow.shader`, `Blend One One`) and high-frequency friction pulsation to blow out pixel contrast right at the contact seam, disguising the horizontal clipping boundary.
  * Rented once per piece feed coroutine and tracked against `TryGetCutterContactSurface` bounds without per-frame garbage allocations, then safely returned in the feed coroutine's `finally` block.


