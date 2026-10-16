# Game Foundation Package

## Purpose

`Packages/com.ardacelik.game-foundation` is the reusable, game-independent foundation extracted from this project. It is deliberately small: it supplies only typed pooling, explicit state transitions, a void event channel, and mobile safe-area fitting.

The package has no dependency on `GravityPuzzle`, `PrototypeBootstrap`, a scene singleton, a board, level assets, or project art. Its namespace is `Arda.GameFoundation` so it can be installed in another game without keeping this game's identity in new production code.

## Ownership boundaries

* A consuming game's composition root creates, registers, and prewarms its pools. `GameObjectPool<T>` owns only rent/return bookkeeping.
* A consuming game declares its own state enum and legal edges. `StateMachine<TState>` validates transitions but does not define game flow.
* Event channels are notifications, never an authority for mutable state. Subscribers own their subscription lifecycle.
* `SafeAreaFitter` owns anchors on one top-level UI container. It does not discover or configure a camera.

## Installation in a later game

Keep this package as a standalone Git repository once its API stabilizes. In the new Unity project's `Packages/manifest.json`, reference an immutable tag:

```json
"com.ardacelik.game-foundation": "https://github.com/<account>/game-foundation.git#v0.1.0"
```

During local package development, place the package under the project's `Packages/` folder and use `"file:com.ardacelik.game-foundation"` instead. Do not copy scripts into a new game's `Assets/Scripts` directory; that would create divergent implementations.

## Intentional exclusions

The following remain game-specific and are not package candidates without a separate, independently designed abstraction:

* gravity grid, piece, shredder, booster, reveal, and level systems;
* level ScriptableObjects and their authored prefabs;
* popup behaviour, tutorial targeting, and PlayerPrefs keys;
* DOTween presentation components and tuning values;
* Layer Lab and all other art or third-party assets.

The existing runtime board construction paths identified in `Architecture.md` as legacy technical debt are explicitly excluded. A later game should use authored prefabs and typed pools instead.

## Next extraction candidates

If a later game needs them, extract a separate presentation package with its own tests and DOTween dependency. Keep only game-neutral parts there, such as button press feedback and panel entrance motion. Do not move the current monolithic `TweenConfig`; its fields are mostly Gravity BlockPuzzle-specific.
