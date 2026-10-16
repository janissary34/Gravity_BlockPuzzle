# Arda Game Foundation

Minimal, game-independent Unity foundations extracted from Gravity BlockPuzzle. It is intentionally not a framework and does not contain board rules, level formats, boosters, scene singletons, or project art.

## Included

* Fixed-capacity typed `GameObjectPool<T>` with an explicit prewarm phase.
* `StateMachine<TState>` with declared legal transitions.
* A reference-friendly `VoidEventChannel` ScriptableObject.
* `SafeAreaFitter` for a top-level mobile UI `RectTransform`.

## Install in a new project

For active local development, place this folder under the new project's `Packages/` directory and add this entry to that project's `Packages/manifest.json`:

```json
"com.ardacelik.game-foundation": "file:com.ardacelik.game-foundation"
```

When this package is moved to its own Git repository, replace the local path with an immutable Git tag:

```json
"com.ardacelik.game-foundation": "https://github.com/<account>/game-foundation.git#v0.1.0"
```

## Integration rules

* Create and prewarm pools from the new game's composition root. Runtime game flow only rents and returns instances.
* Keep each game's state enum and transition list in that game. This package supplies only the generic machine.
* Treat event channels as notifications, never as game-state storage. Every listener unsubscribes during its own teardown.
* Attach `SafeAreaFitter` to a top-level UI container, not to individual controls.
* Add DOTween and presentation components in the consuming game. DOTween is deliberately not a dependency of this foundation package.

## Excluded on purpose

Gravity grids, pieces, boosters, shredders, level assets, input rules, popup behaviour, tutorial targeting, runtime-built board objects, and third-party art all remain in the game project. The project documentation identifies some runtime construction paths as legacy technical debt; they are not part of this package.
