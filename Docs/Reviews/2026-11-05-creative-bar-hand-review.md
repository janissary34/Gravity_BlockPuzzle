# Independent review: progress bar validation and CreativeHand pointer data (2026-11-05)

This change adds a board frame measurement to `LevelRuntimeRoot`, moves bar placement into a failing pre-recording check, and logs scripted pointer samples. Both reviews are complete and accepted. They also covered the pipeline side. The full texts are stored next to this file.

## Approved exception to the AGENTS.md model requirement

`gemini-4-argon-high` is still not offered. **The user explicitly approved `gemini-3.8-flash-high` for this change only.** The project's review policy is unchanged.

## Execution record

| | Review 1 (proposal) | Review 2 (implementation) |
|---|---|---|
| Command | `agy --sandbox --new-project --model gemini-3.8-flash-high --mode plan --output-format text --print-timeout 300s --print "<prompt>"` | same |
| Working directory | a fresh temp folder outside the repository with read-only plain copies (0 symlinks) | same, with both repositories' actual diffs, tests, decisions and validation evidence |
| Material passed | directly in the prompt | directly in the prompt |
| `--add-dir` / `--dangerously-skip-permissions` | not used | not used |
| Exit / duration | 0 / 252 s | 0 / 274 s |
| Output | complete written analysis with final verdict; no tool requests, denial or timeout | same |
| Context or repository changed by reviewer | no (SHA-256 of context files; `git status`) | no |
| SHA-256 of review text | `59506ea1…70de93` | `42e76483…8492e6` |
| Verdict | APPROVE WITH CHANGES | APPROVE WITH CHANGES |

## Findings and decisions

### Review 1 (proposal)

| # | Severity | Finding | Decision |
|---|---|---|---|
| H1 | High | Hand must follow clip speed and holds | **Adopted.** Sampled per master frame through the clip map |
| H2 | High | Gap field changes meaning | **Adopted.** The only asset using it was updated; it has produced no imported take |
| M1 | Medium | Frame measurement needs explicit injection | **Adopted.** The bootstrap passes `LevelRuntimeRoot` to the session |
| M2 | Medium | Log capacity could grow during drags | **Adopted.** Sized from `maxSeconds` and frame rate |
| M3 | Medium | Editor safe area is the whole view | **Documented** |
| M4 | Medium | Short gaps and clip edges | **Adopted.** Keys clamp to the clip; a gap under `min_glide_f` keeps the finger down |
| M5 | Medium | Renderers without sprites | **Adopted** |
| L1–L3 | Low | Repeat reads, enum order, licence tracking | **Adopted.** Licence notes are on the assets and in the pipeline docs |

### Review 2 (implementation)

| # | Severity | Finding | Decision |
|---|---|---|---|
| M1 | Medium | A failed check leaves the bar moved | **Adopted.** It is restored before returning, with a test |
| L1 | Low | A short approach had no fade | **Adopted**, with a test |
| L2 | Low | Interpolation before the first sample | **Adopted**, with a test |
| L3 | Low | Cropped clips | **Declined: incorrect.** The spec's transform has no crop field |
| Gap 1 | Test | Timer not covered | **Adopted.** `VisibleTimer_IsKeptClearOfTheFrame` |
| Gap 2 | Test | Failure path tested without the coroutine | **Accepted.** `Complete()` leaves Play Mode, which stalls the test runner, so tests call the check directly |
| Gap 3 | Test | Pointer JSON format | **Declined.** A real run's events file was parsed and imported by `cpl` |

## Validation evidence

- **Rendered frames, isolated copy of the real Scene1:** L30, L30 with timer, L31 (14×8), L33 (11×10), L1 (4×4). The bar is outside the board with a small gap on all five, and the timer clears the frame. Images: `CreativePipeline/cache/verification/bar-framing/five-configs.png`.
- **Calibration:** the frame sprites include transparent padding, measured at 0.31–0.37 world units on four levels, so `boardFrameArtPadding` is 0.3.
- **Levels:** L40 and L46 did not build in those runs; the same levels failed at the game's default framing in the pool test (pre-existing capacity limit).
- **Pointer data, isolated run of the unchanged script:** 42 samples, both drags accepted, script frames unchanged.
- **Compile:** Assembly-CSharp 220 files OK, Assembly-CSharp-Editor 43 files OK.
- **Unity tests:** EditMode 51 of 54, PlayMode 20 of 31 passed. All 14 failures are the stale tests in `Docs/KnownIssues.md`.
- **Real recording:** `gbp_L30_20261105-115408` with `L30_MinimalCenteredTake`: validation passed, 42 pointer samples, both drags accepted, and drag, release and shred frames equal to `gbp/take/L30-003`. The bar ends 47 px above the visible frame, with 183 px above the bar and about 190 px below the shredder.
