# Gravity BlockPuzzle Engineering Contract

This file governs all refactor work in this repository.

## Delivery workflow

1. Audit the affected code before implementation and report files to change, remove, and add.
2. Stop for user approval after the audit and before writing gameplay refactor code.
3. Work in approved phases only. Every completed phase must compile and preserve playable drag, fall, and shredding behaviour.
4. At the beginning of each phase, state the participating classes, their responsibilities, config ownership, communication path, state flow, and pooling needs.
5. Report only phase completions unless a decision or permission is required.

## Runtime restrictions

- Do not use runtime `Instantiate` or `Destroy`; use the pooling system. Prewarm is the sole instantiation exception.
- Do not use `GameObject.Find`, `FindObjectOfType`, `FindObjectsOfType`, `Resources.Load`, `SendMessage`, runtime `AddComponent`, or uncached `Camera.main`.
- `GetComponent` is allowed only once in `Awake` or `OnEnable` to cache a dependency. Never call it in `Update`, loops, event handlers, or hot paths.
- Normal board movement and placement must not use raycasts or colliders as game-rule authority.
- Physics handoff is coordinate/state driven, not trigger driven. Normal piece colliders are disabled and their rigidbodies are not simulated.

## Architecture

- Use composition and single-purpose classes. Do not introduce inheritance chains or base piece classes.
- Keep game logic in plain C# where possible. MonoBehaviours adapt Unity/presentation only.
- Use interfaces or event channels for inter-system communication; avoid global singletons. If unavoidable, hide behind an interface.
- `PrototypeBootstrap` is the composition root unless an already-installed DI container is found and already used in the project. Do not introduce a DI framework.
- Piece lifecycle: `Spawned -> Placed -> Dragging -> Falling -> HandoffToPhysics -> Shredding -> Despawned`. Invalid transitions warn and do not mutate state.
- Game lifecycle: `Initialize -> Ready -> Playing -> LevelComplete -> Result`.

## Required data and presentation

- Board rules use an authoritative grid/matrix state.
- Pieces come from `BlockPiece` prefab variants through a typed pool. A piece prefab has all required components ready.
- Use `IPool<T>`, `GameObjectPool`, `PoolService`, and `IPoolable` (`OnSpawn`, `OnDespawn`) or a demonstrably equivalent typed design.
- Apply colours/sprites through `PieceVisualConfig`; use `MaterialPropertyBlock` where compatible.
- Read grid, gravity, shredder, tween, and pool values from ScriptableObject configs. Do not add magic numbers.
- Tweens belong only to presentation. Link each tween with `SetLink(gameObject, LinkBehaviour.KillOnDisable)`; default to `SetAutoKill(true)`. Reusable tween instances require `SetAutoKill(false)`, `Rewind`, and pool ownership.
- Configure `DOTween.SetTweensCapacity` during bootstrap. Do not create tweens every frame.

## Performance

- Avoid per-frame allocations, LINQ in hot paths, recurring string construction, repeated UI rebuilds, and unnecessary coroutines.
- Minimise `Update` users; prefer a single tick runner for gameplay progression.
- Preallocate hot-path collections and use NonAlloc APIs only where physics remains an approved presentation requirement.
- Prefer readability and explicit ownership over speculative abstractions or unused wrappers.

## Project layout for new refactor code

```
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

Namespaces mirror these folders. Do not add unused generic frameworks, wrappers, or future-facing code.
