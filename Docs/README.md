# Gravity BlockPuzzle Documentation

This directory contains durable technical knowledge that is useful beyond a single task or code review.

`AGENTS.md` at the repository root is the authoritative engineering contract. Documents here describe the current implementation, important invariants, known defects, and the rationale behind architectural decisions. They must not weaken or override that contract.

## Documents

* `Architecture.md`: current system ownership, state authority, composition, and verified legacy boundaries.
* `GameFoundation.md`: reusable package boundaries and installation guidance for future games.
* `Presentation.md`: board, piece, obstacle, renderer-slot, and visual fallback rules.
* `KnownIssues.md`: verified unresolved defects and architectural gaps.
* `Decisions/`: architectural decision records for choices that should remain understandable after the original implementation context is gone.

Current accepted decisions include presentation authority boundaries and the
scene-authored hybrid level-geometry root.

## When to update

Update these documents in the same change when code alters:

* authoritative state ownership or system communication;
* lifecycle or pooling behaviour;
* configuration or asset-authoring ownership;
* a public contract used by multiple systems;
* a subtle invariant whose violation can recreate a known class of bugs;
* or a verified known issue.

Do not add entries for minor visual tuning, temporary investigations, routine refactors with no contract change, or unverified theories.
