# Gravity BlockPuzzle Engineering Contract

This file governs all gameplay, systems, architecture, performance, maintenance, and future development work in this repository.

## Engineering principles

All production code must be modular, structured, scalable, clean, maintainable, and performance-conscious.

* Modules must have clear boundaries and single responsibilities.
* Dependencies and ownership must be explicit.
* Systems must remain extensible without requiring large rewrites.
* Prefer simple, readable solutions over unnecessary abstraction.
* Performance-sensitive gameplay paths must avoid unnecessary allocations, lookups, polling, and repeated work.
* Do not sacrifice architecture or maintainability for short-term implementation convenience.
* New features must extend the existing architecture rather than bypassing or duplicating established systems.
* Avoid parallel implementations of systems that already have an established owner.
* Prefer explicit data flow and predictable state changes over hidden side effects.

## Development workflow

1. Audit affected systems before making significant architectural or gameplay changes.
2. Preserve existing playable behaviour unless the task explicitly requires changing it.
3. Keep changes scoped, compile-safe, and compatible with the established architecture.
4. Before introducing a new system, major feature, or architectural change, identify:

   * participating classes,
   * responsibilities,
   * config ownership,
   * communication paths,
   * state flow,
   * lifecycle requirements,
   * and pooling requirements.
5. Do not introduce architectural patterns, dependencies, frameworks, or abstractions that conflict with this contract.
6. Extend existing systems when appropriate instead of creating duplicate implementations.
7. Every completed development step must leave the project in a compilable state and preserve unrelated gameplay behaviour.
8. Report only meaningful implementation completions unless a technical decision, architectural conflict, or permission is required.

## Runtime restrictions

* Do not use runtime `Instantiate` or `Destroy`; use the pooling system. Prewarm is the sole instantiation exception.
* Do not use `GameObject.Find`, `FindObjectOfType`, `FindObjectsOfType`, `Resources.Load`, `SendMessage`, runtime `AddComponent`, or uncached `Camera.main`.
* `GetComponent` is allowed only once in `Awake` or `OnEnable` to cache a dependency. Never call it in `Update`, loops, event handlers, or hot paths.
* Normal board movement and placement must not use raycasts or colliders as game-rule authority.
* Physics handoff is coordinate/state driven, not trigger driven.
* Normal piece colliders are disabled and their rigidbodies are not simulated.
* Do not introduce polling when an explicit event, state transition, command, or owned tick path can handle the same responsibility.
* Avoid hidden runtime dependency discovery.

## Architecture

* Use composition and single-purpose classes.
* Do not introduce inheritance chains or base piece classes.
* Keep game logic in plain C# where possible.
* MonoBehaviours adapt Unity, scene references, input, presentation, lifecycle, or engine-specific behaviour only.
* Use interfaces or event channels for inter-system communication; avoid global singletons.
* If a singleton is unavoidable, hide it behind an interface and keep access centralized.
* `PrototypeBootstrap` is the composition root unless an already-installed DI container is found and already used in the project.
* Do not introduce a DI framework.
* Dependencies should be supplied explicitly from the composition root whenever practical.
* Ownership of mutable state must be clear and singular.
* Avoid multiple systems independently mutating the same authoritative state.
* Presentation must not become the source of truth for gameplay rules.
* New systems should integrate through existing contracts, state, events, services, or configuration instead of directly reaching into unrelated implementations.

### Piece lifecycle

`Spawned -> Placed -> Dragging -> Falling -> HandoffToPhysics -> Shredding -> Despawned`

* Invalid transitions warn and do not mutate state.
* State transitions must remain explicit.
* Presentation effects must react to lifecycle state; they must not determine lifecycle state.

### Game lifecycle

`Initialize -> Ready -> Playing -> LevelComplete -> Result`

* Gameplay systems must respect the active game lifecycle.
* Systems must not independently invent conflicting game-state flags when the lifecycle already represents the required state.

## Required data and presentation

* Board rules use an authoritative grid/matrix state.
* World transforms and visuals are representations of board state, not the source of truth.
* Pieces come from `BlockPiece` prefab variants through a typed pool.
* A piece prefab has all required components ready before runtime.
* Use `IPool<T>`, `GameObjectPool`, `PoolService`, and `IPoolable` (`OnSpawn`, `OnDespawn`) or a demonstrably equivalent typed design.
* Apply colours and sprites through `PieceVisualConfig`.
* Use `MaterialPropertyBlock` where compatible instead of unnecessary material instances.
* Read grid, gravity, shredder, tween, pool, and other gameplay tuning values from ScriptableObject configs.
* Do not add unexplained magic numbers to production gameplay code.
* Feature-specific tuning values should have one clear configuration owner.
* Do not duplicate the same configurable value across unrelated components unless there is a deliberate reason.

## Tween and presentation rules

* Tweens belong only to presentation.
* Gameplay state must never depend on a tween completing successfully unless an explicit presentation-to-gameplay contract has been intentionally designed.
* Link each tween with:

`SetLink(gameObject, LinkBehaviour.KillOnDisable)`

* Default to:

`SetAutoKill(true)`

* Reusable tween instances require:

  * `SetAutoKill(false)`,
  * `Rewind`,
  * explicit lifecycle ownership,
  * and compatibility with pooling.
* Configure `DOTween.SetTweensCapacity` during bootstrap.
* Do not create tweens every frame.
* Avoid repeatedly rebuilding identical tween sequences in hot paths when they can be safely reused.
* VFX, animation, audio, and tween systems must not directly mutate authoritative board state.

## Performance

* Avoid per-frame allocations.
* Avoid LINQ in hot paths.
* Avoid recurring string construction in gameplay loops.
* Avoid repeated UI rebuilds.
* Avoid unnecessary coroutines.
* Avoid repeated component lookups.
* Avoid unnecessary scene traversal.
* Avoid repeated collection creation in hot paths.
* Minimise `Update` users.
* Prefer a single tick runner or explicitly owned update path for gameplay progression.
* Systems that do not require continuous updates should be event/state driven.
* Preallocate hot-path collections.
* Reuse buffers where practical.
* Use NonAlloc APIs only where physics remains an approved presentation requirement.
* Pool frequently created gameplay and presentation objects.
* Do not optimize cold paths at the expense of readability without evidence that the optimization is useful.
* Prefer readability and explicit ownership over speculative abstractions or unused wrappers.

## State and data integrity

* Each authoritative gameplay value must have one clear owner.
* Avoid mirrored mutable state unless synchronization is explicit and necessary.
* Never infer authoritative board occupancy from visual transforms.
* State changes must occur through owned APIs or explicit transitions.
* Events communicate that something happened; they should not become hidden mutable state containers.
* Invalid states should fail safely, warn clearly, and avoid partial mutation.
* Avoid boolean combinations that recreate an implicit state machine when an explicit state already exists.

## Feature development rules

* New features must integrate with existing architecture rather than bypassing it.
* Before adding a new manager, service, controller, coordinator, or global access point, verify that an existing system does not already own that responsibility.
* Do not create duplicate gravity, grid, pooling, input, shredder, piece-state, or lifecycle authorities.
* Extend existing interfaces only when the new responsibility genuinely belongs to them.
* Do not turn existing interfaces into unrelated catch-all APIs.
* Prefer adding a focused component or service over expanding an unrelated class.
* Keep gameplay, presentation, infrastructure, and configuration responsibilities separated.
* Feature removal should leave no dead configuration, unused event subscriptions, abandoned prefabs, or unreachable code.

## Dependency rules

* Dependencies should flow from composition/root systems toward concrete runtime implementations.
* Core gameplay logic should not depend on presentation implementations.
* Presentation may observe gameplay state through stable interfaces, events, or read-only data.
* Infrastructure services must not silently become gameplay authorities.
* Avoid circular dependencies.
* Avoid static mutable state unless the data is truly process-wide and stateless alternatives are impractical.
* Do not introduce service locators as a substitute for explicit dependency ownership.

## Pooling rules

* Runtime-spawned reusable gameplay objects must come from pools.
* Pool ownership must be explicit.
* Pooled objects must fully reset their mutable runtime state on spawn/despawn.
* Event subscriptions, tweens, physics state, visual state, timers, and temporary references must not leak between pool usages.
* Prewarm expected gameplay pools during initialization where appropriate.
* Do not return an object to a pool while another system still owns or references it.
* Pool APIs should remain typed where possible.

## Configuration rules

* Use ScriptableObject configuration for designer-tunable gameplay and presentation values.
* Runtime state must not be stored in shared ScriptableObject assets unless intentionally designed as runtime state containers.
* Config objects describe behaviour; runtime objects own mutable session state.
* Do not hardcode values that are expected to vary by level, piece type, difficulty, platform, visual theme, or balancing pass.
* Configuration naming must clearly indicate the system it belongs to.

## Code quality

* Use clear, intention-revealing names.
* Keep methods focused and reasonably small.
* Avoid classes that coordinate unrelated responsibilities.
* Avoid utility classes that become dumping grounds.
* Do not add wrappers that provide no meaningful abstraction.
* Do not add generic frameworks for hypothetical future requirements.
* Remove obsolete code instead of leaving commented-out implementations.
* Comments should explain non-obvious intent or constraints, not restate obvious code.
* Prefer explicit code over clever code.
* Do not silently swallow exceptions or invalid state.
* Warnings and errors should contain enough context to identify the affected system or object.

## Project layout for new gameplay and system code

```text
Assets/Scripts/

  Bootstrap/

  Config/

  Core/Grid/

  Core/StateMachine/

  Core/Events/

  Gameplay/Pieces/

  Gameplay/Gravity/

  Gameplay/Input/

  Gameplay/Shredder/

  Presentation/Views/

  Presentation/Tween/

  Presentation/VFX/

  Infrastructure/Pooling/

  Infrastructure/Services/
```

* Namespaces mirror these folders.
* Place new code according to responsibility, not convenience.
* Do not create new top-level architecture folders without a real responsibility boundary.
* Do not add unused generic frameworks, wrappers, or future-facing code.
* Existing project structure may be extended when a genuinely new responsibility requires it, but new folders must represent a clear architectural boundary.

## Compatibility rule

When modifying an existing feature:

* preserve unrelated behaviour,
* preserve established public contracts unless a change is necessary,
* update all affected callers when a contract intentionally changes,
* do not leave temporary compatibility layers without a clear need,
* and do not introduce a second implementation solely to avoid integrating correctly with the existing architecture.

The goal is not merely to make features work.

The repository must remain modular, structured, scalable, clean, maintainable, performant, and predictable as the game grows.
