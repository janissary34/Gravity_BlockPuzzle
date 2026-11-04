# Independent review: Creative Mode scripted input and release events (2026-11-04)

The review required by AGENTS.md before modifying `PuzzleDragController` (a performance-sensitive hot path) and after implementing the change. Both reviews are complete and accepted. The full review texts are stored next to this file (`*-review-1-proposal.gemini.md`, `*-review-2-implementation.gemini.md`; local temp paths in them are shortened to `review-context/`).

## Approved exception to the AGENTS.md model requirement

AGENTS.md names `gemini-4-argon-high`. That model is not available on the Antigravity account in use (agy 1.5.0 offers `gemini-3.8-flash-high/medium/low`, `claude-sonnet-4-6`, `claude-opus-4-6-thinking`, `gpt-oss-120b-medium`). **The user explicitly approved `gemini-3.8-flash-high` as a substitute for this review only.** The project's review policy is unchanged.

## Execution record

| | Review 1 (proposal) | Review 2 (implementation) |
|---|---|---|
| Command | `agy --sandbox --new-project --model gemini-3.8-flash-high --mode plan --output-format text --print-timeout 300s --print "<prompt>"` | same |
| Working directory | a fresh temp folder outside the repository containing read-only plain copies (0 symlinks, 0 writable files): AGENTS.md, proposed diff, current `PuzzleDragController.cs`, proposed new files, `CreativeSession.cs`, Creative Mode docs | fresh temp folder: AGENTS.md, actual `git diff` of `Assets/Scripts`, new files, review 1 |
| `--add-dir` / `--dangerously-skip-permissions` | not used | not used |
| Exit / duration | 0 / 231 s | 0 / 295 s |
| Output | complete written analysis with final verdict; no tool requests, no denial, no timeout | same |
| Context changed by reviewer | no (SHA-256 of every file compared before/after) | no |
| Repository changed by reviewer | no (`git status` unchanged) | no |
| SHA-256 of review text | `e34afc8e…a3b38e` | `f0a3eeae…461321` |
| Verdict | APPROVE WITH CHANGES | APPROVE WITH CHANGES |

Earlier attempts that did **not** produce a review and were not treated as one:
1. Authentication timeout before the CLI was signed in.
2. `gemini-4-argon-high` not recognised by the account.

## Findings and decisions

### Review 1 (proposal)

| # | Severity | Finding | Decision |
|---|---|---|---|
| 1 | Critical | `PieceReleased` not raised when a joint-group snap fails or the restore fallback fails | **Adopted.** Piece id is captured at the top of `ReleasePiece`; all five exits raise exactly one event |
| 2 | Critical | A drag starting on the previous drag's release frame is dropped; a press on a busy board fails silently | **Adopted.** `CreativePointerScript` rejects scripts without a ≥1-frame gap; a scripted press that selects no piece logs a warning |
| 3 | High | `IsActive` stays true forever and blocks device input | **Adopted.** Active only until the last release |
| 4 | High | World→screen→world round trip | **Declined.** The capture size is fixed at Play Mode start, before the level builds; the round-trip error is negligible; screen coordinates keep parity with device input and serve the hand overlay; scripts are authored in board cells (cell centres). Review 2 agreed this is justified |
| 5 | Medium | Clear the injected source on disable; session cleanup | **Adopted** (`OnDisable`, `CreativeSession.End`) |
| — | Note | Frame pacing between FixedUpdate and Update can shift cascade completion by ±1 frame | **Adopted as authoring guidance:** leave settle gaps between drags (`CreativeScript` docs) |

### Review 2 (implementation)

| # | Severity | Finding | Decision |
|---|---|---|---|
| 1 | Critical | Coroutines resume after `Update`, so anchoring the script when it starts skips script frame 0 (a drag at frame 0 would be lost) | **Adopted.** Script frame 0 is anchored lazily on the first frame the controller samples it |
| 2 | Low | `piece.released` JSON lacks the piece id | **Declined: incorrect.** `CreativeEventLog.Write` writes `piece_id` for every event that has one (shared prefix, not in the diff the reviewer saw) |
| 3 | Low | Validate `startFrame ≥ 0`; a null level escapes the `ArgumentException` catch | **Adopted as explicit validation** (negative start frame, missing script or level → `ArgumentException`). A catch-all was not added, because AGENTS.md forbids silently swallowing errors |

The reviewer also confirmed:
- exactly one release event per release on all five exits;
- no normal-play regression (one null check per frame when no source is injected);
- no per-frame allocations;
- compliance with AGENTS.md (composition root injection, single input authority, no new Update users, folder and namespace layout).

## Validation evidence

- Headless compile of Assembly-CSharp (219 files) and Assembly-CSharp-Editor (38 files) after each step: OK (`creative-pipeline/tools/unity/compile_check.py`).
- Not yet run, because the Unity Editor has not yet imported the changes: the existing PlayMode tests, a manual mouse play check, and a scripted take comparing two runs' `events.json`. Results will be added to `Docs/CreativeMode.md`.
