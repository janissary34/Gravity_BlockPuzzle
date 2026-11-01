# Architecture

## Authority and composition

* `PrototypeBootstrap` is the scene composition root.
* The board grid/matrix is authoritative for occupancy and gameplay rules.
* Piece transforms, colliders, sprites, VFX, and tweens represent gameplay state; they do not define it.
* Haptics are presentation-only. `HapticFeedbackConfig` owns cue tuning and per-cue gates; `RuntimePieceFactoryBootstrap` configures the centralized runtime adapter. Gameplay owners notify it only after a successful authoritative outcome. Shredder feedback begins at real cutter contact, aggregates concurrent feeds by their least feed progress, and always stops on cancellation, app pause, preference disable, or scene teardown.
* Runtime piece roots come from the typed `BlockPiece` pool and must fully reset presentation, physics, subscriptions, and temporary state when returned.
* `GravityLevelDefinition` and the associated ScriptableObject configs own authored level and tuning data. Mutable session state belongs to runtime objects. Each level also owns its authored difficulty label (`Normal`, `Hard`, or `Very Hard`); this is presentation metadata, with `Normal` reserved for the future Easy UI indicator. Level definitions own viewport coverage; `BoardPresentationConfig` owns the shared board-top viewport inset, which runtime applies through the gameplay camera when it builds every level.

Partially opened Tunnels use the explicit content-activation and presentation-lease contract in `Decisions/ADR-003-partial-box-content-activation.md`. The reveal coordinator owns content activation records and Box progress counters. A completed shredder handoff or a Hammer removal of an entire authoritative piece reports its stable piece ID to that coordinator; partial Hammer topology edits do not count. A Tunnel view may report only monotonic shutter exposure. A renderer, collider, or transform never decides whether a content piece exists, may move, or participates in gravity.

See `Decisions/ADR-001-presentation-is-not-gameplay-authority.md` for the reasoning behind the presentation boundary.

## Startup splash and transition cover

`Splash.unity` is the build's entry scene. Its `LoadingScreen` prefab uses the
art-authored `SplashScreen` sprites and `SplashScreenController`. The controller
marks its root GameObject with `DontDestroyOnLoad` so it persists across subsequent
scene loads as a transition cover. During game boot, `Scene1` loads asynchronously
and activates immediately without artificial display holds. For subsequent level
advances, retries, and transitions, `SplashSceneLoader` routes directly through
`SplashScreenController`'s transition cover: the loading overlay displays over
the active scene, `Scene1` reloads asynchronously in a single hop, and
`PrototypeBootstrap.StartForActiveScene` calls `NotifyLevelReady` once the board
is completely constructed to dismiss the cover. Legacy gameplay bootstrap objects in
`Splash.unity` remain disabled so `RuntimePieceFactoryBootstrap` in `Scene1` is the
sole composition root.

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

`BlockShredder` owns admission to each authored `ShredderCatchZone`, but does
not reserve a queue, lane, or grid column. A piece whose authoritative footprint
reaches the map-bottom exit in that zone enters `HandoffToPhysics` immediately:
targeting is locked and its grid cells stay reserved while an owned kinematic
exit phase clears the map. The exit line is evaluated from the lowest physical
cell edge, never from a piece root/pivot. At that line the reservation is
released and the piece becomes an independent Dynamic freefall body. Freefall
bodies may collide and rotate naturally, but config-owned speed and angular-speed
limits bound contact recovery. They are never held in a hidden queue.

The mouth capture plane uses the same lowest-cell geometry. Capture promotes a
piece to the existing kinematic feed without retaining its freefall colliders;
therefore a feeding piece cannot act as an infinite-mass obstacle for pieces
still in freefall. Its downward speed eases to the configured feed speed and its
orientation settles toward its nearest cardinal rotation at a bounded rate,
rather than snapping every body toward zero degrees. This keeps irregular and
board-rotated shapes visually continuous through the state transition.

`ShredderCatchZone` validates physical capture using per-cell geometry and then
confirms the authoritative grid state. `CollisionBounds` is not used to decide
capture or candidate order for irregular shapes. Pieces supported above a feed
may remain in normal `Placed`/occupied grid state or enter a prevalidated
temporary physics island. In both cases each piece is admitted independently
when its own coordinate reaches the catch zone. Temporary Unity physics never
owns gameplay admission or queue progress. The physics mouth has vertical guides
only: support members freeze lateral movement and rotation on handoff, and their
lowest cell crossing the capture half-space promotes them immediately. No
horizontal shelf may retain a member below the cutter.

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
