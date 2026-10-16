# Architecture

## Authority and composition

* `PrototypeBootstrap` is the scene composition root.
* The board grid/matrix is authoritative for occupancy and gameplay rules.
* Piece transforms, colliders, sprites, VFX, and tweens represent gameplay state; they do not define it.
* Runtime piece roots come from the typed `BlockPiece` pool and must fully reset presentation, physics, subscriptions, and temporary state when returned.
* `GravityLevelDefinition` and the associated ScriptableObject configs own authored level and tuning data. Mutable session state belongs to runtime objects.

See `Decisions/ADR-001-presentation-is-not-gameplay-authority.md` for the reasoning behind the presentation boundary.

## Current level construction

`GravityLevelRuntime` computes the selected level's scene representations when a level starts:

* board background and composite board frame;
* map-boundary collision geometry;
* obstacle collision objects;
* merged or sliced obstacle presentation objects;
* reveal presentations, shredders, pins, and pooled pieces.

Board, frame, map collision, and obstacle representation components are owned by
the scene-authored `LevelRuntimeRoot` prefab. The prefab preallocates renderer and
collider slots in Edit Mode. Runtime construction only renames, positions, sizes,
enables, and skins those existing components; it does not instantiate these
objects or add their components.

The following hierarchy groups therefore remain visible outside Play Mode:

* `Board Background`
* `Composite Board Frame`
* `Map Collision - Obstacle Boxes`
* `Obstacle Collisions`
* `Obstacle Visuals`

Obstacle collision and obstacle presentation are intentionally separate. Collision objects own static physics contact; merged and sliced visual objects render the connected shape. Neither presentation object is board-state authority.

`RuntimePieceFactoryBootstrap` supplies the root explicitly to
`GravityLevelRuntime`. If the reference is absent or a level exceeds an authored
capacity, construction fails with a contextual error rather than silently
creating fallback objects.

Use `Gravity Puzzle/Level Authoring/Rebuild Level Runtime Root` after adding or
resizing campaign levels. The editor command validates every level in the
configured sequence, expands capacity when required, saves
`Assets/Prefabs/LevelRuntimeRoot.prefab`, and reconnects the active scene.

## Remaining legacy boundary

Generated pin presentation and collision still use their older
runtime-construction paths. Reveal presentations, piece connections (joint springs),
shredders, and pieces use prewarmed typed pools. The remaining pin path is
technical debt and must not be copied into new features.

See `Decisions/ADR-002-scene-authored-hybrid-level-geometry.md` for ownership and
performance trade-offs.
