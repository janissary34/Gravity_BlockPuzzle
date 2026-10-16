# ADR-002: Scene-Authored Hybrid Level Geometry

Status: accepted.

## Context

Level assets define responsive board topology, so storing one fully baked scene
hierarchy per level would duplicate level truth and risk divergence. The older
implementation instead created board, frame, map-collision, and obstacle objects
and components during Play Mode. That hid important geometry outside Play Mode,
added runtime construction work, and conflicted with the repository contract.

## Decision

Use one scene-authored `LevelRuntimeRoot` prefab as reusable component storage.

* The prefab owns preallocated `SpriteRenderer` and `BoxCollider2D` slots plus
  the static map `Rigidbody2D` and `CompositeCollider2D`.
* `GravityLevelDefinition` remains the single source of topology and level data.
* `GravityLevelRuntime` retains responsive geometry calculations, but applies
  their results to authored slots instead of creating objects or components.
* `RuntimePieceFactoryBootstrap` supplies the root explicitly.
* The editor rebuild command sizes the prefab against every campaign level and
  stores a first-level preview that is visible outside Play Mode.
* Capacity exhaustion is an authoring error. Runtime must not instantiate a
  hidden fallback.

## Consequences

* Board and obstacle hierarchy, sprites, and colliders can be inspected before
  Play Mode.
* Level startup avoids the former GameObject and component creation work for
  board, frame, map collision, and obstacles.
* Scene/prefab deserialization still has a cost; preallocation moves and makes
  that cost predictable rather than eliminating it.
* New or substantially enlarged campaign levels require rebuilding the root.
* Board matrix state remains authoritative. Renderer and collider slots are
  adapters and must never become level data.
