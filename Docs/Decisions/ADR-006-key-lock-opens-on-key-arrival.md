# ADR-006: A Lock opens when its Key's flight arrives

## Status

Accepted

## Context

A Key piece and a Lock piece are authored as a pair. While the Key piece is on
the board, the Lock piece cannot move. When the Key piece is destroyed, its
Key icon flies to the centre of the Lock, and the Lock should break at that
moment. Opening the Lock at destruction time would let the player drag or
drop the Lock piece while the Key is still in the air. ADR-001 forbids
gameplay that depends on a tween unless an explicit
presentation-to-gameplay contract is designed.

## Decision

The Key flight is an explicit presentation-to-gameplay contract:

* `KeyLockCoordinator` (owned by `PrototypeBoard`) consumes the Key on the
  board's authoritative piece-destruction notification and records it, so the
  pair's gameplay outcome is fixed at that point.
* The Lock piece stays `IsKeyLocked` until its flight sequence **completes**.
  Its `OnComplete` reports the arrival; the coordinator then calls
  `PuzzlePiece.TryReleaseKeyLock` and wakes gravity.
* If no flight can be presented (no sprite, no free presentation slot, no
  `TweenConfig`), the Lock opens immediately. A flight is killed only when
  the Lock piece leaves the board (Rocket carry or pool return), where no
  opening is required, so no timeout or polling is needed.
* A Hammer hit or a Lock activated after its Key was consumed (Box content)
  opens the Lock directly, without a flight.

## Consequences

* The Lock's open time follows `TweenConfig` Key flight values, which stay
  short.
* The flight renderer is borrowed from the Lock piece's own pooled
  presentation slots. It therefore needs no prefab or pool of its own and is
  cleaned up with the Lock piece.
