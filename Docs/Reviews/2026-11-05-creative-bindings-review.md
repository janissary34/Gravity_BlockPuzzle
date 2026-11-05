# Independent review: Scene1 UI bindings authoring tool (2026-11-05)

The fix changes what `Gravity Puzzle/Creative/Author Scene1 UI Bindings` writes to Scene1 and which UI a take hides, so it was reviewed before coding and after implementation. Both reviews are complete and accepted. The full texts are stored next to this file (`*-review-1-proposal.gemini.md`, `*-review-2-implementation.gemini.md`).

## Approved exception to the AGENTS.md model requirement

`gemini-4-argon-high` is still not offered (`agy models`, 2026-11-05). **The user explicitly approved `gemini-3.8-flash-high` for these two reviews only.** The project's review policy is unchanged.

## Execution record

| | Review 1 (proposal) | Review 2 (implementation) |
|---|---|---|
| Command | `agy --sandbox --new-project --model gemini-3.8-flash-high --mode plan --output-format text --print-timeout 300s --print "<prompt>"` | same |
| Working directory | a fresh temp folder outside the repository with read-only plain copies (0 symlinks): proposal, AGENTS.md, the tool, `CreativeSceneBindings`, a `CreativeSession` excerpt, the docs section, the failed Editor log, two scene probes | fresh temp folder: actual diff, the tool, the tests, validation evidence, review 1, AGENTS.md |
| Material passed | directly in the prompt | directly in the prompt |
| `--add-dir` / `--dangerously-skip-permissions` | not used | not used |
| Exit / duration | 0 / 155 s | 0 / 147 s |
| Output | complete written analysis with final verdict; no tool requests, denial or timeout | same |
| Context changed by reviewer | no (SHA-256 of every file compared before/after) | no |
| Repository changed by reviewer | no | no (`git status` unchanged) |
| SHA-256 of review text | `7306fa1c…5cb57` | `9cfc7e13…4cc1f` |
| Verdict | APPROVE | APPROVE WITH CHANGES |

## Findings and decisions

### Review 1 (proposal)

| # | Severity | Finding | Decision |
|---|---|---|---|
| 1 | Low | Inline `string[][]` paths are noisy | **Adopted.** A small `Path(...)` helper |
| 2 | Low | Sibling traversal must be deterministic | **Adopted.** `GetChild` in index order; a second run is byte-identical |
| 3 | Info | Keep `backdropRenderers` intact | **Confirmed.** The tool only writes `uiGroups` |
| 4 | Info | `BoardOnly` does not cover unmapped UI | **Documented** in `Docs/CreativeMode.md` |

### Review 2 (implementation)

| # | Severity | Finding | Decision |
|---|---|---|---|
| 1 | Medium | The new test file is not in the diff, so it could be left out of the commit | **Adopted.** It was untracked; committed with its `.meta` |
| 2 | Low | `internal` access from the tests | **No change.** `Assets/Tests/Editor` has no asmdef, so the tests share Assembly-CSharp-Editor; they compile and pass |
| 3 | Info | Duplicate-match logging is useful | No change |

## Validation evidence

All Unity runs used an isolated copy of the project, not the working checkout. The tool was not run on the repository's Scene1, which is unchanged by this commit.

- **Compile:** Assembly-CSharp 219 files OK, Assembly-CSharp-Editor 41 files OK.
- **Tool on real Scene1:** no MISSING path; 0 null references after reopening; bootstrap field assigned; `backdropRenderers` unchanged.
- **Graphics per group:** LevelHeader 3, DifficultyBadge 3, Timer 4, ProgressBar 2, Coins 4, BoosterBar 42, SettingsButton 3, RetryButton 3, TutorialAndRewardPopups 84, WinPanel 31, FailAndRevivePanels 80, DebugButtons 2.
- **Repeat run:** byte-identical scene. **Unsaved scene:** refused, file unchanged.
- **Scene1 by YAML document id:** 242 → 480. None removed. Changed: the bootstrap (`creativeSceneBindings`, plus `hapticFeedbackConfig: {fileID: 0}`, a field already null but absent from the YAML) and `SceneRoots` (one root). Added: the bindings object (3 documents) and 235 stripped stubs for Graphics inside prefab instances. The line diff is large because Unity re-sorts documents on save.
- **Unity tests with the bindings applied:** EditMode 45 of 48 passed, PlayMode 13 of 24 passed. All 14 failures are the stale tests in `Docs/KnownIssues.md`. The 4 new `FindAll` tests and 4 Creative timer tests pass.
- **Tool in the user's Editor:** the first run saved Scene1 with the same group counts and the same document-level change as the copy (0 removed, the same 2 changed, 238 added).
- **Not yet run:** a real take with the profile applied.
