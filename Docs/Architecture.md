# Architecture

## Authority and composition

* `PrototypeBootstrap` is the scene composition root.
* The board grid/matrix is authoritative for occupancy and gameplay rules.
* Piece transforms, colliders, sprites, VFX, and tweens represent gameplay state; they do not define it.
* Haptics are presentation-only. `HapticFeedbackConfig` owns cue presets, per-cue cooldowns, priorities, and the global minimum interval; `RuntimePieceFactoryBootstrap` configures the centralized `HapticFeedbackRuntime`. Gameplay owners notify it only after a successful authoritative outcome. `HapticFeedbackRuntime` decides whether a cue plays (preference, cooldown, global interval with higher-priority bypass); the third-party `Assets/MOST_HapticFeedback` package is the only device backend (iOS UIKit presets / Android waveforms, its own Xcode post-process and VIBRATE manifest). Shredder feedback is a repeating preset tick driven by `BlockShredder`'s feed tick from real cutter contact until every concurrent feed ends, and haptics stop on app pause, preference disable, or scene teardown.
* Level completion is committed by `PrototypeBoard` as soon as every authoritative piece is `Despawned` and no reveal content is pending: it enters `LevelComplete` and raises the cleared events in that frame. `LevelProgressManager`'s final voxel flights and slider fill are presentation only and never gate that transition; they gate only the win panel reveal, which opens when they finish or after `TweenConfig.LevelCompleteRevealMaxDelay`, whichever comes first.
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
targeting is locked and `PrototypeBoard` atomically clears its grid footprint
and enters `OpenFreefall` at that same exit boundary. There is no transform-
driven approach phase: Rigidbody2D is the only movement owner below the map
until the cutter seam. This prevents an invisible reservation from making
pieces above wait and prevents a second motor from slowing or repositioning
the freefall body.

`PieceModel` is the sole gameplay lifecycle authority. `PuzzlePiece` is the sole
Unity-side writer of normal-piece Rigidbody2D, collider, material, and layer
profiles, applying a full profile only after that lifecycle changes. `Grid` is
non-simulated with colliders disabled; drag selection resolves the occupied
authoritative grid cell rather than a physics overlap. `OpenFreefall` is Dynamic on
`ShredderPhysicsFreefall` with full geometry and can collide only with other
pieces on that layer; `Shredding` disables simulation and colliders before the
controlled cutter presentation starts. In particular, open freefall
must never inherit the support-island layer, feed material, blocker contacts, or
guide contacts. While the model is `OpenFreefall`, `BlockShredder` owns only its
vertical descent speed and keeps it on its map-exit X with zero rotation (a
straight drop; any drift is restored and logged once). No grid reservation,
queue, drag, or targeting logic owns it after that release.

Grid drag, gravity presentation, and release snap are transform-owned by
`PieceGridFallView`; they do not tween the non-simulated grid Rigidbody2D. Gravity
falls accelerate from a start speed to a terminal speed (`TweenConfig` grid-fall
values); a retarget keeps the current speed. A piece captured in the same frame its
fall arrives enters `OpenFreefall` with that arrival speed (never below
`ShredderConfig.PhysicsFreefallEntrySpeed`) and keeps accelerating to
`PhysicsFreefallMaxSpeed`, so the drop has no visible speed reset. When
the presentation completes, `PuzzlePiece.SetGridPresentationPosition` synchronizes
the dormant body pose with the committed grid transform before any later handoff.

The freefall capture plane is the rendered cutter seam rather than the older
approach plane. Only when the lowest real cell crosses that seam does the board
transition the already-off-board model from `OpenFreefall` to `Shredding`.
This transition never recreates a grid reservation. It ends freefall physics and
starts the established controlled cutter feed, which owns row-by-row clipping,
visual tremor, pooled debris, HUD progress, and final pool return. It does not
take transform or Rigidbody ownership before the cutter transition, so it cannot
block or slow a piece in the open freefall area.

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
