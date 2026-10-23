# Architecture

## Authority and composition

* `PrototypeBootstrap` is the scene composition root.
* The board grid/matrix is authoritative for occupancy and gameplay rules.
* Piece transforms, colliders, sprites, VFX, and tweens represent gameplay state; they do not define it.
* Runtime piece roots come from the typed `BlockPiece` pool and must fully reset presentation, physics, subscriptions, and temporary state when returned.
* `GravityLevelDefinition` and the associated ScriptableObject configs own authored level and tuning data. Mutable session state belongs to runtime objects.

Partially opened Boxes use the explicit content-activation and presentation-lease contract in `Decisions/ADR-003-partial-box-content-activation.md`. The reveal coordinator owns content activation records; a Box view may report only monotonic shutter exposure. A renderer, collider, or transform never decides whether a content piece exists, may move, or participates in gravity.

See `Decisions/ADR-001-presentation-is-not-gameplay-authority.md` for the reasoning behind the presentation boundary.

## Shredder admission and temporary physics

Grid gravity batches independent falling groups when their occupied grid columns
are disjoint. Ordinary pieces resting solely on a falling ordinary piece can
also be committed as a temporary support stack, without changing their joint
graph. Each included member must have the same safe fall distance and compatible
fall presentation; independently supported members stay behind. Other groups
sharing columns retain sequential planning to prevent visual intersections.
Downwards step traversal in grid gravity planning consults movement legality
(`RevealAreaCoordinator.CanMovePiece`) at each step, preventing pieces from
tunneling through or entering closed boxes or locked elevator boundaries.
If a candidate group fails commit, it is excluded for the batch so unrelated
independent groups continue to fall without stalling board gravity.
The board commits the stack atomically and owns the lifecycle transitions.

`BlockShredder` owns admission to each authored `ShredderCatchZone`. Feed
reservations are scoped to an active piece and zone. Their authoritative grid
columns must be disjoint for concurrent admission; overlapping columns remain
serial. Current cell bounds additionally guard physical clearance. If an
independent candidate encounters a tilted active feed, the active feed is
straightened toward its grid orientation before retrying admission. While
multiple feeds share a zone, their feed rotation is held at its current angle;
once only one remains, its configured rotation resumes. Physical bounds can
still delay admission when a body is displaced or the conservative bounds overlap.

`ShredderCatchZone` validates physical capture using per-cell geometry and then
confirms the authoritative grid state. `CollisionBounds` is not used to decide
capture or candidate order for irregular shapes. A temporary
`ShredderPhysicsIsland` still owns only the board-derived dependent stack: its
members remain grid-reserved while Unity renders their short-lived contact
reaction. The pooled `ShredderPhysicsMouth` retains its shelf and guides; when
independent islands share a zone, the guides can grow upward without moving the
cutter or shelf plane.

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

`SceneTunaBoardFrame.prefab` is nested under `Composite Board Frame` inside that
runtime-root prefab. It is an editable artist baseline and pre-authored slot
owner for the modular frame. `GravityLevelRuntime` configures those slots from
the selected level's perimeter (corners first, then directional edge runs); its
sprites are presentation-only and do not own map collision or grid topology.

The following hierarchy groups therefore remain visible outside Play Mode:

* `Board Background`
* `Composite Board Frame`
* `Map Collision - Obstacle Boxes`
* `Obstacle Collisions`
* `Obstacle Visuals`

Obstacle collision and obstacle presentation are intentionally separate. Collision objects own static physics contact; merged and sliced visual objects render the connected shape. Neither presentation object is board-state authority.

Reveal-area and joint-spring presentation instances are prewarmed typed pools.
`GameObjectPool` owns the rented-instance inventory and can return a whole pool
at level reset; views return themselves immediately when their presentation
completes. Presentation coordinators must not maintain a second active-instance
list, because that duplicates pool state and risks stale pooled views.

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
