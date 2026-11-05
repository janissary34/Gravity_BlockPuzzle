# Independent review: Creative Mode framing and Minimal UI profile (2026-11-05)

This change adds `GravityLevelRuntime.ConfigureBoardTopViewportInset`, a bootstrap call, and progress bar placement in `CreativeSession`, so it was reviewed before coding and after implementation. Both reviews are complete and accepted. The full texts are stored next to this file.

## Approved exception to the AGENTS.md model requirement

`gemini-4-argon-high` is still not offered (`agy models`, 2026-11-05). **The user explicitly approved `gemini-3.8-flash-high` for these two reviews only.** The project's review policy is unchanged.

## Execution record

| | Review 1 (proposal) | Review 2 (implementation) |
|---|---|---|
| Command | `agy --sandbox --new-project --model gemini-3.8-flash-high --mode plan --output-format text --print-timeout 300s --print "<prompt>"` | same |
| Working directory | a fresh temp folder outside the repository with read-only plain copies (0 symlinks) | same, with the actual diff, new files, decisions and validation evidence |
| Material passed | directly in the prompt | directly in the prompt |
| `--add-dir` / `--dangerously-skip-permissions` | not used | not used |
| Exit / duration | 0 / 213 s | 0 / 204 s |
| Output | complete written analysis with final verdict; no tool requests, denial or timeout | same |
| Context or repository changed by reviewer | no (SHA-256 of context files; `git status`) | no |
| SHA-256 of review text | `68304bf8…296ed4` | `a0113541…fe451c` |
| Verdict | APPROVE WITH CHANGES | APPROVE WITH CHANGES |

## Findings and decisions

### Review 1 (proposal)

| # | Severity | Finding | Decision |
|---|---|---|---|
| 1 | Major | Static override can leak; call the setter unconditionally from the bootstrap | **Partly adopted.** It is reset in `ResetLevelSequence`, which runs at every Play Mode start, even with domain reload disabled. The unconditional call adds nothing and would touch the normal path, so it was declined. A test covers the reset |
| 2 | Major | Rebuild `LevelRuntimeRoot` to 280+ visual slots; clamp the range | **Partly adopted.** `[Range]`, tooltip and docs added. The rebuild was declined: it changes a shared normal-game prefab, L30 uses 110 of 242, and exhaustion fails loudly at build |
| 3 | Minor | Coordinate-space and teardown safety | **Adopted.** Canvas-plane conversion, cached position, null-guarded restore. The bar's parent has no layout group |
| 4 | Minor | Timer visibility is coupled to the gap | **Tooltip and docs adopted.** Custom editor validator declined as unnecessary |
| 5, 6 | Minor, advisory | Docs and field ranges | **Adopted** |

### Review 2 (implementation)

| # | Severity | Finding | Decision |
|---|---|---|---|
| 1 | Major | Override leaks between takes and into normal play in one session | **Declined: incorrect.** Each take is one Play Mode session, and the reset runs at every Play Mode start. A scene reload inside a take keeps the same framing, which is correct |
| 2 | Major | Clamp non-zero insets to at least 0.12 | **Declined.** A silent clamp hides the configured value. A level over capacity throws at build, so the take fails loudly. Limits are documented on the field and in `Docs/CreativeMode.md` |
| 3 | Minor | Fall back to the session camera when the canvas has none | **Adopted** |
| 4 | Minor | No tests for bar placement and restore | **Adopted.** `CreativeFramingPlayModeTests` |

## Validation evidence

All Unity runs used an isolated copy of the project.

- **Compile:** Assembly-CSharp 220 files OK, Assembly-CSharp-Editor 42 files OK.
- **Pool use, sandbox spike on 68 levels at 9:16:** L30 at inset 0.16 uses 110 of 242 visual slots and 28 of 244 frame slots. The same 3 levels fail at every inset tested, including the game's 0.24 (pre-existing).
- **Bindings tool:** no MISSING path. Scene1 changes by document from 480 to 481: only the bindings object changes (`progressBar`, `progressBarCanvas`), plus one stripped RectTransform. None removed.
- **Take config:** L30, `CreativeUI_Minimal` (10 groups), inset 0.16, gap 0.05.
- **Unity tests:** EditMode 48 of 51 passed, PlayMode 15 of 26 passed. All 14 failures are the stale tests in `Docs/KnownIssues.md`. The 3 new EditMode tests, 2 new PlayMode tests and 4 Creative timer tests pass.
- **Not yet run:** a real take, compared with `gbp/take/L30-003`.
