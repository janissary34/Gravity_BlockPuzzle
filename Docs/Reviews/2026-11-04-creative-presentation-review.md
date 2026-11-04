# Independent review: Creative Mode presentation fixes (2026-11-04)

This review covers four changes:
- Frozen-timer lifecycle: a new `PrototypeBoard.TimerStarted` event;
- creative display level number: `GravityLevelRuntime.DisplayLevelNumber`, used by the HUD and win labels;
- Scene1 UI bindings and profiles;
- recording start on `CreativeSession.CaptureReady`.

AGENTS.md requires the review before changes to gameplay classes (`PrototypeBoard`, the composition root) and after implementation. Both reviews are complete and accepted. The full texts are stored next to this file (`*-presentation-review-1-proposal.gemini.md`, `*-presentation-review-2-implementation.gemini.md`; local temp paths in them are shortened to `review-context/`).

## Approved exception to the AGENTS.md model requirement

`gemini-4-argon-high` is still not offered on the Antigravity account. **The user explicitly approved `gemini-3.8-flash-high` again, for these reviews only.** The project's review policy is unchanged.

## Execution record

| | Review 1 (proposal) | Review 2 (implementation) |
|---|---|---|
| Command | `agy --sandbox --new-project --model gemini-3.8-flash-high --mode plan --output-format text --print-timeout 300s --print "<prompt>"` | same |
| Working directory | a fresh temp folder outside the repository with read-only plain copies (0 symlinks, 0 writable files): proposal, AGENTS.md, Creative Mode docs, excerpts of `PrototypeBoard`, `RuntimePieceFactoryBootstrap`, `GravityLevelRuntime`, full Creative session/config/bindings/recorder files, `SplashScreenController` | fresh temp folder: actual `git diff`, new files (authoring tool, tests, profile), proposal, review 1, decisions |
| Material passed | directly in the prompt | directly in the prompt |
| `--add-dir` / `--dangerously-skip-permissions` | not used | not used |
| Exit / duration | 0 / 227 s | 0 / 231 s |
| Output | complete written analysis with final verdict; no tool requests, denial or timeout | same |
| Context changed by reviewer | no (SHA-256 of every file compared before/after) | no |
| Repository changed by reviewer | no (`git status` unchanged) | no |
| SHA-256 of review text | `381cc595…eb397d` | `6ab97e6f…21a2bc` |
| Verdict | APPROVE WITH CHANGES | APPROVE WITH CHANGES |

## Findings and decisions

### Review 1 (proposal)

| # | Severity | Finding | Decision |
|---|---|---|---|
| 1 | High | The win panel ("Level N Completed!", "LEVEL N+1") would still show the campaign number | **Adopted.** `GravityLevelRuntime.ConfigureDisplayLevelNumber` / `DisplayLevelNumber` (reset with the level sequence) are used by every player-facing label. Gameplay gating and rewards keep `CurrentLevelNumber` |
| 2 | High | `SetActive(false)` kills linked tweens and coroutines and misses popups opened mid-take | **Adopted, different mechanism.** Bindings hold explicit `Graphic` references (authored in Edit Mode); hiding disables those components. Canvas/CanvasGroup were rejected: most targets have no own Canvas, adding components would change the game's UI prefabs, and `PanelEntrancePresentation` animates alpha |
| 3 | High | Static event lifecycle: duplicate subscribers, pending request leak | **Adopted.** Subscribe at `EnteredPlayMode` only when a take is pending; one-shot unsubscribe in the handler; unsubscribe and clear at `ExitingPlayMode`, with an error if no signal arrived; take-name check |
| 4 | Medium | `CreativeTimerFreeze` and `CreativeCaptureSignal` are unnecessary wrappers | **Adopted.** The freeze lives in `CreativeSession` (`TimerStarted` and `Playing` → `HoldFrozenTimer`); `CaptureReady` is an `#if UNITY_EDITOR` static event on `CreativeSession` |
| 5 | Low | Rename the scene object `level_txt_bg (1)` | **Declined.** It would edit the game's scene objects for tooling convenience; the authoring tool reports a missing path and changes nothing |

### Review 2 (implementation)

| # | Severity | Finding | Decision |
|---|---|---|---|
| 1 | Low | `enumValueIndex` assumes contiguous enum values | **Adopted** (`intValue`) |
| 2 | Low | The capture-delay coroutine is not stopped in `End()` | **Adopted** (handle kept and stopped) |
| 3 | Low | Static `CaptureReady` could keep stale subscribers when domain reload is disabled | **Adopted.** Reset at `SubsystemRegistration`, which runs before the driver subscribes at `EnteredPlayMode` |

The reviewer also confirmed:
- the implementation matches the proposal and decisions;
- normal gameplay is unchanged (no request means no override and no subscribers; `TimerStarted` has no listeners in normal play);
- no per-frame allocations;
- the four PlayMode tests cover the `TimerStarted` contract, normal countdown, the synchronous Frozen hold with release on `End`, and Normal timer mode.

## Validation evidence

- Headless compile after the final changes: OK for Assembly-CSharp (219 files), Assembly-CSharp-Editor (40 files) and the PlayMode test assembly (`creative-pipeline/tools/unity/compile_check.py` plus the test assembly's own Unity response file).
- In-Editor results (PlayMode tests, bindings authoring, new take) are recorded in `Docs/CreativeMode.md`.
