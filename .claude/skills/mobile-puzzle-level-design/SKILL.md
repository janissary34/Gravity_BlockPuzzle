---
name: mobile-puzzle-level-design
version: 1.0.0
description: >-
  Design, sequence, generate, audit, and rebalance levels for mobile puzzle games
  (sorting, matching, queue, shooter, screw, layered-object, factory, grid, and jam).
  Use for level-duration bands, difficulty curves, hard/super-hard cadence,
  onboarding, obstacle introductions, boosters, near-win/fail evaluation,
  session architecture, ad/reward pacing, solver validation, and live tuning.
---

# Mobile Puzzle Level Design — AI Skill

**Status:** Evidence-informed working standard, not a universal benchmark.
**Intended use:** Internal game design and AI-assisted level planning.
**Core principle:** A level is a unit of experience (teach, master, challenge, recover, reward), not merely an arrangement of objects.

> **Gravity BlockPuzzle:** Bu skill'i bu projeye uygularken önce `Docs/LevelDesignSkill.md` dosyasını oku; projenin gerçek mekanikleri, tanıtım seviyeleri ve mevcut sapmalar orada. Proje kuralları (AGENTS.md, CLAUDE.md) bu dokümanın genel önerilerinin önüne geçer. Kaynak yapıştırma bölüm 6'daki "Boosters Overview" listesinde kesik geldi; eksik kısım yok sayılmalı.

## 0. Operating contract for an AI using this skill

When asked to design or rebalance a puzzle game, do not start by randomizing boards. Follow this sequence:

1. **Classify the product:** FAST (short levels / progression + interstitial), STAGED (several short boards within one numbered level), LONG (extended tactical level / rescue & RV) or HYBRID. Define whether the game uses a timer, limited slots, move budget, or lives.
2. **Write the mechanical grammar:** goal, supply/queue, access/reveal, capacity, ordering, fail conditions, rescue, layer rules. State what information players possess before an irreversible choice.
3. **Choose a measurable target curve:** clean-run duration by level slice, first-attempt win rate by level class, retry burden, loss-progress distribution, and allowable uninterrupted time between natural monetization checkpoints. Use the tables below as initial hypotheses only.
4. **Design the curriculum:** make a mechanic prerequisite map; assign INTRODUCE → PRACTICE → VARY → COMBINE → TEST → REST across levels. Plan every new mechanic/booster separately from difficulty spikes.
5. **Author the progression matrix:** levels/bands × length × difficulty × mechanic exposures × economy/monetization checkpoint × expected emotion. Preserve individual special-level overrides.
6. **Generate complete level records,** then validate solver feasibility, deterministic seeds, opening clarity, recoverability, and endgame.
7. **Simulate full player journeys** (not just independent levels): time to next offer/ad, recovery cost, failures, replays, event payoffs, and natural session-ending opportunities.
8. **Review recorded outcomes** and change one primary variable at a time. Mark every claim as OBSERVED, INTERNAL, HYPOTHESIS, or VALIDATED.

If inputs are missing, make explicit labeled assumptions; do not present a tentative default as a measured market fact. If the requester provides game-specific metrics or exceptional levels, those override generic recommendations. Never claim the spreadsheet data proves a causal retention or revenue effect.

**Required output for a fresh game:** (a) product/architecture brief, (b) level-band matrix, (c) first 30–50-level sequence, (d) mechanic curriculum, (e) hard/super-hard and fail plan, (f) monetization/session timeline, (g) level record/schema, (h) solver/QA/telemetry validation plan.

## 1. Evidence baseline — what we actually observed

The attached evidence set contains Pixel Flow levels 1–300, six Playliner puzzle games (Car Match, Goods Puzzle, Magic Sort, Block Jam, Hole People, Knit Out), five screw-puzzle games (Screw Away 3D, Screw Master 3D, ScrewSort 3D, Woodle Screw Jam, Screwdom 3D), and a 20-page Hexa Sort level-design case study. That is 12 game titles in the quantitative review. See the Appendix for methodology, quantitative slices, and observed intro sequences.

**Recorded median successful-level time (seconds):**

| Game | L1–15 | L16–50 | L51–100 | L101–200 |
|---|---|---|---|---|
| Pixel Flow | 52 | 72 | 90 | 164 |
| Car Match | 32 | 58 | 54 | 97 |
| Magic Sort | 49 | 72 | 74 | 89 |
| Hole People | 29 | 67 | 84 | 132 |
| Block Jam — stages aggregated | 72 | 155 | 195 | 169* |
| Knit Out | 54 | 136 | 178 | 170* |
| Goods Puzzle | 204 | 276 | 351* | — |
| Screw Away 3D | 397 | 430 | — | — |
| Screw Master 3D | 303 | 363 | — | — |
| ScrewSort 3D | 308 | 310 | 282* | — |
| Woodle Screw Jam | 165 | 344 | 280* | — |
| Screwdom 3D | 332 | 431 | — | — |

\* = partial band, limited observed levels. These are descriptive medians, not recommended duration targets. In Playliner, the time is from a successful attempt, across three non-paying test accounts; screw tests use four participant profiles with scripted RV/non-RV behavior; Pixel Flow reports one recorded level-play time. Different play protocols and player populations must not be combined into a single average. For Block Jam, rows 3.1, 3.2, 3.3 describe stages of level 3, so stage durations are summed per actual numbered level before comparison.

**The key pattern:** duration, designed challenge, and first interstitial are independent variables. Pixel Flow's 300-level review has 216 Normal, 45 Hard, 39 Very Hard levels; hard/very-hard labels become common only after its first 23 normal levels. Car Match and Magic Sort use conspicuous Hard at levels ending in 4, Very Hard at those ending in 9, starting at 14/19. No such fixed pattern is universal: Knit Out varies, and the screw-puzzle workbook contains titles with no recorded hard labels.

**Google Sheet completeness caveat:** The supplied link to gid=1357357794 (identified in prior discussion as Match Factory by Peak) was not retrievable in this run. No Match Factory measurements or rules are attributed to that tab. Do not infer its contents from Pixel Flow or the other workbooks. Import that tab when available and re-evaluate the duration/cadence comparisons.

## 2. Define the level's six mechanical layers

| Layer | Designer question | Parameters to expose to AI |
|---|---|---|
| Goal | What clears the level? | target boxes/objects/colors/units, goal stages |
| Supply | What can be selected and in what order? | queue, spools, blocks, color mix, counts, seeds |
| Access | Which items are visible/usable? | object layer, frontmost rule, line of sight, locks, reveal dependencies |
| Capacity | Where can the player hold or route resources? | trays, bench/slots, buffers, overflow, free-slot trajectory |
| Constraints | What makes a choice dangerous? | tied groups, generators, hidden items, durability, timer, move cap |
| Rescue | How can a mistake be reversed? | undo, shuffle, selection, add slot, magnet, revive, checkpoint |

Compute duration and difficulty separately. Volume (number of knots / screws / matches) drives actions/time; ordering, deadlock risk, information gaps, interacting blockers, and low resource slack drive decision difficulty. A very long but obvious level is not necessarily hard. A very short level with a single lethal dependency can be much harder.

For each authored level, identify its signature moment: first match, cascade, hidden reveal, unlock, near-full bench recovery, satisfying chain, or final color clear. Do not pad minutes with repetitive low-value actions.

## 3. How long should levels take? — working launch targets

**Metric definitions:** `clean_run_sec` = gameplay time of the winning attempt, excluding menus and ads. `total_dwell_sec` = gameplay time across all failed and winning attempts until clear or abandon. Benchmark against p50 and p75; do not design only to averages. These target envelopes are HYPOTHESES, not measured norms. Choose one architecture before using the table.

| Level slice | FAST p50 clean run | STAGED p50 full numbered level | LONG p50 clean run |
|---|---|---|---|
| 1–5: show core fun | 15–35s | 30–60s | 45–120s |
| 6–15: onboarding mastery | 30–65s | 60–120s | 120–240s |
| 16–30: first pressure tests | 45–90s | 100–190s | 180–330s |
| 31–50: depth + first combinations | 60–110s | 120–210s | 240–420s |
| 51–100: established loop | 70–140s | 140–260s | 240–450s |
| 101+: mature content | 90–180s | 150–300s | 300–480s |

**Practical p75 guardrails:** FAST onboarding (L1–15) ideally stays below 90s per clean run, STAGED below 180s, LONG below 360s. For mature content, diagnose p75 greater than ~1.5–1.8× p50 as possible duration volatility, opaque blockers, or repetition; it is not automatically a defect. Start measuring total dwell separately because failed attempts can double or triple the true time spent on a level.

**First-level rule:** the first clear should demonstrate the product's main pleasure fast, before a complicated interface lesson. Favor 15–30s in FAST, 30–60s in STAGED, and ~45–90s in LONG. Games sampled sometimes begin much longer (especially screw titles); those are observations, not a reason to copy the burden.

**Hard-level length:** increase useful decisions, not only raw volume. A hard level can be 1.2–1.6× the adjacent normal clean-run duration; a super-hard can be 1.3–1.8× as an initial hypothesis. If it stretches farther, consider checkpoints or stages and measure abandon-on-attempt.

**Within-level arc:**

- **Opening (0–20% progress):** a legible productive move, minimal irreversible risk, visible goal. No immediate hidden trap.
- **Development (20–65%):** the mechanic reveals its consequences, a new relationship emerges, resource slack reduces.
- **Climax (65–90%):** planning and execution matter; challenging levels may tighten late, but all information needed to plan should have been available or safely discoverable.
- **Closure (90–100%):** resolve dependency, accelerate cascading payoff, avoid tedious leftover housekeeping.

For STAGED levels, each substage must create a new visual or mechanical beat. The player-visible level ID, not each stage row, is the unit for completion-time and monetization accounting.

## 4. Difficulty progression — the sawtooth, not a straight line

A useful cycle is easy → medium → hard → relief → fresh variation → medium → super-hard → relief. Over a large band, difficulty trends upward through stronger constraints and combinations, not endless growth in every level's duration.

**Provisional difficulty targets for retained, ad/assist-free users** (first-attempt clear; HYPOTHESIS requiring live calibration):

| Class | First-attempt clear target | Main design purpose | Typical exit |
|---|---|---|---|
| Tutorial / showcase | 95–99% | Learn rule / experience core payoff | Clear and confidence |
| Easy / rest | 90–97% | Feel mastery, get rewarded | Quick clear |
| Medium | 75–90% | Make one or two meaningful plans | Clear with light friction |
| Hard | 55–75% | Test combined understanding | Fair potential for retry / rescue |
| Super Hard | 40–65% | Climax for engaged players | Earned victory or voluntary recovery |

Do not treat these percentages as sourced industry outcomes, and do not target them by forcing a loss. An early challenge with poor retention may need higher first-win probability even when its label says Hard. Measure human win rate on a level by unique starts, not by number of all attempts.

### Suggested label cadence for a new launch

| Band | Difficulty scheduling proposal | Constraint on placement |
|---|---|---|
| L1–10 | All tutorial/easy/soft medium | Teach base system; zero deliberate blockers that require a paid rescue |
| L11–20 | First measured medium; 0–1 hard if mechanics are understood | Do not combine a first mechanic tutorial with a hard spike |
| L21–30 | 1–2 hard + optionally 1 super-hard | First hard often around 20–25; first SH around 29–30 is one option |
| L31–50 | Roughly 2–4 hard + 1–3 super-hard over 20 levels | Leave a clean relief after peaks and a safe intro after new rules |
| L51+ | Start testing ~2 labeled peaks per 10 levels (one Hard, one SH) | Adjust by real fail rate and retention; no forced fixed modulo rule |

Example 10-level motif (not mandatory): E, E, M, E/intro, H, E, M, E, M, SH, followed by a short easy/rest level. A first-time mechanic is not a hard level merely because it looks new. Use tagged special exceptions to prevent a global band rule from overwriting authored tutorial levels.

Three different difficulties: **visual complexity** (how crowded it appears), **cognitive complexity** (what must be planned), and **execution pressure** (timer/actions). Keep them independently configurable. A new visual obstacle can dominate the board while the puzzle itself stays easy.

## 5. Fails, near-misses, and near-wins

A **near-win** is an experience of clear reachable progress (e.g. 75–95% of objective resolved) while one meaningful decision still matters; it may end in a win. A **near-miss failure** is a real, understandable loss after substantial progress, with a visible or reconstructable way to do better. Do not equate a near-win with a scripted loss.

**Design progression:**

- In L1–10, teach cause/effect and protect a first success. Do not manufacture a fail merely to show an offer.
- In L11–30, permit first honest errors, but make the path forward readable; the first hard level should usually be clearable without a booster.
- From L30+, concentrate some tension in the last 20–35% of Hard/SH levels, provided players had enough information and a viable alternative.
- After the first failed attempt, give an actionable reason: which color was stranded, which slot was consumed, which group could have been cleared first, which stage caused failure.
- Offer proportional recovery (undo/selection/add slot/checkpoint/revive) only when it genuinely addresses the failure. An offer after a completely unwinnable state is a content bug, not good monetization.

**Provisional loss-quality tests** (not observed industry targets): On a hard-level loss, examine the fraction of failures with objective progress around 60–90%, especially 75–90% near-win cases; also inspect early failures below 40%. The objective is not to force a precise distribution. Some tested products actually fail predominantly earlier: the supplied loss tables show this plainly. Compare `progress_at_fail`, `elapsed_sec_at_fail`, `retry_rate`, `retry_win_rate`, `post_fail_exit`, and `rescue_takeup` before judging whether a near-miss is satisfying or exhausting.

**Integrity gates:**

- A no-purchase, no-booster valid solution must exist for the advertised rules and tested seeds unless the product explicitly specifies a different mode.
- Do not hide a color/condition that makes the level unwinnable before the player could learn about it.
- If a player replays a failed level, preserve or clearly communicate seed changes; otherwise failure feels arbitrary.
- Do not secretly alter the queue after a player makes a choice to create an unavoidable loss. Keep difficulty design, assistance policy, and economy policy distinct and separately instrumented.
- Diagnose failing at 90% after 7 minutes differently from failing at 90% after 50 seconds: late fail in LONG play has much greater sunk-time cost and may need a fair stage checkpoint.

## 6. Introducing new mechanics: visible dominance ≠ difficulty

The first introduction level should make the new feature obvious, frequent, and easy. This is an important separation: the feature's visual prevalence may be high while constraint pressure is low. A level with nearly every spool tied can be an excellent tutorial if the ties are accessible, consistent, and easy to resolve.

### The six-beat teaching loop

| Beat | Typical relative level | Designer intent | Difficulty budget |
|---|---|---|---|
| Introduce / show | N | Feature occupies much of what player sees; first action demonstrates rule | Easy: high guidance, generous slack |
| Repeat independently | N+1 | Same feature, player acts without a forced hint | Easy: retain low punishment |
| Vary | N+2 | One new arrangement, palette, spatial order, or count | Easy → Medium |
| Contrast / rest | N+3 | Short clear or old mechanic only to prevent overload | Easy |
| Combine | N+4 to N+7 | New feature + one previously mastered mechanic | Medium |
| Test/mastery | N+7 to N+12 | Lower slack / multi-step dependency / meaningful sequencing | Hard only after comprehension validated |

The spacing is flexible; use actual `first_success_with_feature`, `invalid_action_rate`, `hesitation_sec`, and `post_intro_churn` to tune it. Do not schedule two entirely new mechanical rules plus a new booster on one pressured level.

### Example: Tied Spools (a Thread Fever-style queue/bench puzzle)

**Rule:** 2–5 linked spools form a group and clear the tie only after all linked spools have been sorted. The group imposes a dependency; it is not simply a visual decoration.

| Level | Content shape | Why it works |
|---|---|---|
| N: teach | ~70–90% of visible spools tied in simple pairs; 2–3 obvious matching colors; every group accessible; bench has generous capacity; no other new obstacle | Player repeatedly witnesses the tie rule, experiences many successes, almost no decision trap |
| N+1: repeat | Several paired groups, shuffled position, same palette and spare slots | Reinforces rule without tutorial interruption |
| N+2: vary | One three-spool tie; other groups still pairs; slightly more color variety | Teaches the feature's parameter range |
| N+3: rest | Short level with zero or a token tied pair | Preserves pace and gives a mastery reward |
| N+4–6: combine | Tied pair plus an already-known hidden/frontmost queue or simple outer-layer reveal | Requires planning while reusing familiar grammar |
| N+7+: test | One genuinely constrained dependency, tighter bench, but a readable rescue route | First valid Hard candidate |

When new mechanics are "everywhere" on introduction, lower one or more pressure variables to compensate: colors, live branch count, queue uncertainty, capacity pressure, lock depth, total volume, and timer. Before publishing, run a solver on all relevant configurations: linked groups can accidentally produce deadlocks even when colors look easy.

### Mechanic sequencing and frequency

Observed examples: Pixel Flow introduces Surprise Shooter (L8), Connected Shooters (L14), Hard Pixels (L30), Locks/Keys (L40), then a sequence near 15–30 levels apart later. Car Match introduces Tunnel (L6), Box (L10), Curtains (L30); Hole People shows Boxes (L12), Hidden Stickman (L13), Tunnel (L25), Frozen Stickman (L36). These are observations, not guaranteed optimal intervals.

**Initial novelty cadence to test:** L1–15: a new micro-concept every ~3–6 levels, but avoid several simultaneous rule introductions; L16–50: a new primary mechanic roughly every ~8–15 levels; L51–150: ~15–25 levels; L150+: ~20–40 levels unless the game relies more heavily on frequent novelty. Sustain between launches with new combinations, arrangements, goals, visual themes, and special level variants. Do not introduce a feature just because the counter reaches N.

For every feature maintain a **mechanic card**: `first_seen`, `first_compulsory_use`, `first_unguided_success`, `first_variant`, `first_combination`, `first_high_pressure_use`, `prerequisites`, `conflict_matrix`, and `reuse_frequency`. Track introductions per player; a player can miss a mechanic via event routing or skips.

## 7. Boosters and the recovery curriculum

A booster is a solution to a recognizable type of friction. Its first introduction should have an obvious, controlled use case that demonstrates utility without teaching players they must spend to win.

| Problem | Rescue family | Example | First teaching rule |
|---|---|---|---|
| Bench/queue saturation | Capacity | Add Box / extra tray | Show a full-but-recoverable tray, then the extra space creates a visible path |
| Unfavorable queue order | Selection | Hand / Select Tool | Show the needed spool in the queue, accessible via tool |
| Bad future ordering | Reorder | Magic Hat / Shuffle | Show before/after preview if possible; never use unpredictable shuffle as sole solution |
| One missing color | Targeted clear | Magic Spool / Super Shooter | One obvious blocked color, immediate payoff |
| One busy slot | Auto-clear | Magnet | Immediately free a slot and finish a tied dependency |
| Time pressure | Freeze | Time Freeze | Teach in a timed level that remains possible without it |
| Long-level late loss | Stage continuation | Revive / Play On | Preserve meaningful progress and disclose exact effect/cost |

Observed Pixel Flow booster introductions: Extra Tray Slot L7, Select Tool L12, Shuffle L15, Super Shooter L18. The skill should not copy these numbers mechanically; the relevant rule is to expose a booster before its corresponding problem becomes a recurring hard-level bottleneck.

After every first booster teach, run one no-booster level demonstrating that the core puzzle remains achievable, then add a low-friction organic opportunity. Record earned/free availability separately from purchases, ad completions, and uses.

## 8. Session design, monetization checkpoints, and natural stopping points

The failure mode to prevent: a player can spend 60+ minutes crossing levels without a completed ad, rewarded view, meaningful shop/economy moment, or comfortable stopping point. Counting completed levels is insufficient when level length ranges from 30 seconds to 7 minutes. A separate `active_gameplay_clock` is required.

### Three clocks, three decisions

- **Level clock:** time in the current level/attempt, excluding pause, background, and ad time. Determines fatigue and rescue timing.
- **Monetization-opportunity clock:** engaged gameplay time since last eligible, unobtrusive monetization touchpoint. Distinguish offer shown, ad started, ad completed, IAP viewed, purchase — these are not equivalent.
- **Session clock:** active time since session start; drives a natural loop closure and exit opportunity. Record continuous session ID rather than summing all historical level time.

### Candidate first-session architecture (HYPOTHESIS; A/B test)

| Elapsed active time | Desired player beat | FAST | STAGED / LONG |
|---|---|---|---|
| 0–3 min | Core fun / first clear | 2–5 fast clears | First stage/level, reward clear feedback |
| 3–6 min | Competence + variation | Small new rule, optional reward | First checkpoint or clear; optional bonus/reward |
| 6–10 min | A meaningful choice | Optional RV for genuine value; interstitial only when eligible and at safe transition | Between-stage/level reward or transparent rescue/shop choice |
| 10–15 min | A complete mini-arc | Hard or mastery test, relief level, collection/event progress | Climax/end of level, milestone, collectable, natural pause |
| 15–20 min | Return motivation | Reward claim, next unlock teaser, optionally stop | End-of-stage reward, next challenge teaser, comfortable exit |

**Pacing budget to test:** surface an appropriate opportunity at least every ~6–10 minutes of eligible active gameplay, with a clear natural loop closure every ~10–15 minutes. Use noninterruptive earned rewards or information for players who decline ads/purchases. These are product hypotheses, not hard market norms or a promise of monetization. Do not inject ads into an active puzzle move, a first-time mechanic reveal, a chain reaction, a paid no-ads entitlement, or a hard-won victory animation.

**FAST:** after onboarding protection, evaluate a full-screen ad only at legitimate inter-level boundaries, with minimum elapsed-time spacing and session frequency caps. **STAGED:** do not count each substage as a new level to justify ads; reserve interstitials for end of the numbered level or a genuinely safe checkpoint if policy/player experience allows. **LONG:** do not wait for six 7-minute levels; use checkpoint rewards, transparent recovery choices, earned currencies, and optional offers between stages. Rewarded ads should solve a real problem; do not create unavoidable deadlocks to manufacture demand.

**Session ending is not forcing an app exit.** Design soft stopping points after a rewarding clear and event/collection payout; leave a clear next objective and sense of unfinished possibility. The player can continue. A good end beat: win → feel accomplishment → receive progress/reward → see next goal → voluntarily stop or play again. Avoid ending the only play session on an unrecoverable forced fail or an unskippable monetization wall.

**Observed caution:** In Playliner's scripted no-RV tests, first recorded interstitial for Magic Sort was L60 (roughly 81–129 cumulative minutes of level dwell per tester up to that milestone), and for Block Jam was L21 (~41–54 cumulative minutes), but these play hours spanned sessions/days. Do not call these continuous ad-free sessions. The observation demonstrates why a session clock and progression clock must be separately simulated.

### The AI's session-gap simulation (required)

Before approving the first 100-level progression, simulate at least three journeys: (A) first-try wins, (B) occasional failed attempts, (C) repeated hard-level failures without RV/IAP. Use each level's clean-run estimate and retry/abandon assumptions; do not sum clean-run times and call it session duration. Track `active_time_since_eligible_offer`, `active_time_since_completed_ad`, and `active_time_since_soft_stop` separately.

```text
for each simulated session / player profile:
    for each attempted level or substage:
        active_time += simulated_gameplay_and_retry_seconds
        if substage_end or numbered_level_end:
            if offer_eligible and time_since_value_opportunity >= pacing_budget:
                schedule a relevant, optional value exchange at this safe boundary
            if interstitial_eligible and policy_caps_allow and not onboarding_protected:
                consider an interstitial ONLY at a permitted, nonintrusive boundary
            if time_since_soft_stop >= rest_budget:
                deliver a real reward/goal completion + clear optional exit
        record offered / shown / watched / purchased / passed / skipped separately
    report p50, p90 and maximum active time without an opportunity or actual impression
```

Flag for redesign (not automatically force an ad) if any plausible journey contains >~15 minutes of eligible active gameplay with no suitable touchpoint, or >~20 minutes without a natural level/stage closure, unless there is an explicit protected-play design decision. If the player declines rewarded offers, they may legitimately generate zero ad revenue; that outcome must not be "fixed" by creating a forced loss. Diagnose the actual completed-impression distribution separately from offer frequency.

### Session-pacing acceptance checks for every level plan

- Is there a protected, uninterrupted first success in the planned onboarding?
- Does the first meaningful value exchange occur early enough for the actual duration archetype, without manipulating a loss?
- Does the planner simulate active time between monetization events including retries?
- Are there natural reward-and-rest nodes at roughly 10–15-minute intervals, or an explicit different product strategy?
- For ad-eligible users, are impression intervals, offer/accept rates, and policy caps measured separately from merely seeing an offer?
- Can the player reach the next stop point without a mandatory paid purchase/booster?
- Are there separate pathways for premium/no-ads users and for players who never watch RV?

## 9. The level-band progression matrix

Build an editable table with one row per planned band or special level and these columns:

`band | target_duration_p50 | target_duration_p75 | easy% | medium% | hard% | super_hard% | target_first_win% | featured_mechanics | intro_levels | booster_unlocks | planned_loss_quality | economy_pressure | session_checkpoint | override_reason | validation_status`

Default band structure: 1–3, 4–5, 6–10, 11–15, 16–20, 21–29, 30, 31–39, 40, 41–50, 51–75, 76–100, 101–150, 151–200, 201+. This is a template, not mandatory. Always split bands at mechanic introductions, hard/super-hard peaks, significant rewards, and special exceptions; a clean round number must not hide an authored exception.

### Illustrative first 30-level rhythm for a FAST/STAGED title

| Levels | Experience / curriculum | Pressure |
|---|---|---|
| 1–3 | Core mechanic, immediate gratifying clear, clear success feedback | Very easy |
| 4–6 | More colors/volume one axis at a time; one independent decision | Easy |
| 7–10 | First rescue tool or first simple novelty; repeat and rest | Easy / Soft Medium |
| 11–13 | Mechanic A dominant but easy, practice, vary | Easy |
| 14–17 | Familiar combination and one medium check; recovery lesson | Easy → Medium |
| 18–20 | A short mastery test, then reward/rest; no first-time complexity pileup | Medium → Easy |
| 21–23 | Mechanic B (e.g. tied spools): teach → practice → variation | Easy |
| 24 | First Hard candidate: known mechanics, no unseen rules | Hard |
| 25–27 | Relief and recombination; visible improvement on previous challenge | Easy → Medium |
| 28–29 | Preparation, first Very/Super Hard candidate if ready | Medium → SH |
| 30 | Short, easy recovery or a carefully isolated new feature | Easy |

**Sequence rule:** After a hard/SH peak, give relief before piling on novelty. After a new mechanic's introduction, reinforce before demanding mastery. If observed onboarding churn increases, make the entire first 30 levels easier before expanding late-game complexity.

## 10. Make the puzzle solvable and the difficulty intentional

For every candidate level, run these gates:

- **Structural:** objective units and corresponding resources balance; all colors/items have a path; tied groups, hidden queues, layers, locks, and generators obey the engine; no illegal overlaps.
- **Solver:** at least one legitimate path to victory for tested seeds without forced purchase or hidden assistance; record solution witness, decision depth, branch factor, and deadlock reachability.
- **Player comprehension:** the first playable action teaches or reinforces the intended rule, not an arbitrary trap.
- **Fairness:** all critical hazards are visible or signaled; risky new interactions are tested at low pressure.
- **Variety:** sequence does not repeat the same constraint combination to boredom.
- **Instrumentation:** stage, level, seed, version, introduced mechanics and outcome are logged.

For procedural generation, generate level families by changing one variable at a time:

- Hold volume/palette fixed; change queue order → isolate strategic challenge.
- Hold queue fixed; change volume → isolate duration.
- Hold both fixed; change bench capacity → isolate failure pressure.
- Hold all fixed; change hidden/tied proportion → isolate feature comprehension.
- Hold early 70% fixed; alter late 30% → isolate endgame spike.

Use deterministic seeds and keep a `special_level_override` with precedence over global bands. Never use a single scalar difficulty index as the only authoring input. Keep duration, dependency depth, uncertainty, capacity pressure, execution pressure, and relief visible individually.

## 11. Telemetry: measure the design, not the label

Log at least:

| Event/metric | Minimum fields or calculation |
|---|---|
| level_start | user, session, level_id, version, seed, class, role, flags, mechanics |
| level_attempt_end | win/fail/abandon, attempt, gameplay_sec, progress_completed/required, failure_reason |
| level_clear | total_dwell_sec, clean_run_sec, boosters, RV, paid/earned rescue, next-level-start |
| board_state_snapshot | remaining colors/goals, free slots, queue preview, layer revealed, safe moves |
| mechanic_first_use | mechanic_id, first_seen_level, first_correct_action, confusion/error/hint, independent_clear |
| booster_offer → booster_use | trigger, problem state, source/cost, accepted, outcome |
| monetization_event | session_active_sec, since_previous_offer, offer/show/start/complete/revenue; eligibility |
| soft_stop / session_end | active_session_sec, cause, last win/fail, reward claimed, return prompt |

**Dashboard:** p50/p75/p90 clean and total durations by level slice, unique first-attempt win, eventual clear, median attempts per clear, progress-at-loss distribution, first-fail level, early/mid/late fails, tutorial error rate, rescue efficacy, next-level start, abandonment on level, active minutes to first offer and to first completed monetization event, session length, ad impressions per eligible active hour, and IAA/IAP contribution.

**Important segmentation:** first-time vs experienced, no-RV vs RV, payer vs nonpayer, interstitial eligible vs ad-free, assistance on/off, country/cohort, device, seed, version, level class, and stage. Later-level survivor samples are not equivalent to the initial cohort. Do not include assisted wins in a claim about natural puzzle difficulty.

### Diagnose before changing levels

| Signal | Suspected cause | First experiment |
|---|---|---|
| High duration, high first-win, low continuation | Boredom or volume overload | Reduce repeated actions while preserving decisions |
| Low duration, high fail | Early trap / small capacity / unreadable rule | Add one safe opening path or one slot |
| Losses cluster below 40% | Opening comprehension or unforgiving bottleneck | Reorder reveal/queue, improve hint and signage |
| Losses cluster 80–95% and players abandon | Punishing late-game stake, weak revive/checkpoint | Shift part of pressure earlier or preserve progress |
| Booster used but win still low | Rescue doesn't address constraint | Map offer to actual bottleneck state |
| Users clear levels quickly but play 60m without ads/offers | Monetization clock tied only to completed levels | Add safe elapsed-time checkpoint or higher-value optional RV |
| New feature produces hesitation/exit | Tutorial is visually dominant and cognitively hard | Keep dominance, remove constraint pressure, add no-risk demonstration |
| Hard labels without higher fail/retry | Cosmetic difficulty flag | Recalibrate label or puzzle constraints |

## 12. AI level record — minimum contract

Each authored level should be exported in machine-readable format consistent with `templates/level_record.yaml`. Required fields:

```yaml
level_id: 24
level_version: 1
level_class: hard
sequence_role: test
architecture: FAST
mechanic_objective: "Use tied-group ordering learned at levels 21–23"
timing:
  target_clean_p50_sec: 95
  target_total_dwell_p75_sec: 180
challenge:
  intended_first_attempt_win_rate: 0.65  # hypothesis, not a forced outcome
  target_loss_progress_window: [0.60, 0.90]  # analytical lens, never scripted failure
  pressure_location: endgame
features:
  new_rules: []
  known_rules_used: [tied_spools, color_matching, queue_order]
  primary_blocker: tied_spools
integrity:
  win_without_purchase: true
  tested_seed_count: 0
  solver_witness: null
session:
  planned_transition: hard_clear_then_relief
  optional_value_offer_at_safe_boundary: true
metadata:
  evidence_class: HYPOTHESIS
  special_override: true
  override_reason: first_hard_mastery_check
```

`null` means not yet verified, never zero; replace `tested_seed_count: 0` with actual verification results before release. The record must include game-specific content and constraints; it is invalid to pass the bare illustrative record as a playable board.

### AI review / rejection rubric

Reject if a rule is taught with unresolvable pressure; if win depends on an undisclosed paid booster, on a favorable untested random seed, or on a hidden rule; if the timeline creates unsafe monetization interruptions; if a level's duration target is copied from an unrelated product; or if a late failure has no understandable cause. Rework if level role, primary decision, fail route, or next-level transition are unspecified. Accept for live test only after solver, human comprehension, seed and telemetry checks pass. Validate with controlled traffic and continue tuning.

## 13. Practical output format when asked for a whole progression

1. Architecture statement (FAST / STAGED / LONG, timer, stage policy, primary monetization model, first-session goal).
2. Benchmark table (exactly which games support which observed numbers; source quality).
3. Duration bands with p50 clean, p75 total, expected attempt counts.
4. Difficulty chart: exact locations of first hard/SH and number per 10; each peak has a relief successor.
5. Mechanic-release calendar: first seen, show-dominance %, practice, combine, mastery, booster relation.
6. Session timeline in minutes and levels with safe offers, event payout, natural stop, caps.
7. First 30–50 levels in a row-per-level table with explicit exception handling.
8. Level-generation template + constraint/interaction rules + solver checklist.
9. A/B hypotheses and telemetry; mark unknown assumptions and unverified claims.

If a source is inaccessible, add it to Outstanding evidence and still produce a complete provisional skill from verified inputs. When additional game deconstructions are added, update the evidence log first; change recommendations only after evaluating whether the new game matches the same architecture and testing protocol.

## 14. Sources & provenance

- **Pixel Flow Deconstruction (2)(1).xlsx** — Yağız Gür; Introduction, 300-level Level Design Deconstruction, blockers, booster schedule, palette. User-uploaded, inspected in this task.
- **Playliner_Puzzle_Games_iOS(1).xlsx** — Readme, Games_overview, Losses, offers, six titles; iOS, three non-paying testers, minimum hours and recording protocol in readme. User-uploaded; internal-use restriction in source.
- **Screw Puzzle Analysis(1).xlsx** — Readme, five data_... tabs, Losses, Ads, Monetization_interactions, Boxes colors, stores; four tester profiles with controlled RV/non-RV behavior. User-uploaded; internal-use restriction in source.
- **HaleTurer_CaseStudy_LevelDesigner(1).pdf** — Hexa Sort case study, particularly pp. 5 (staged levels), 8 (good opening + hints), 9–10 (difficulty and roller-coaster), 13 (testing), 17–19 (churn and monetization). Its proposals and anecdotal results are author experience, not outcome measurements from the other workbooks.
- **Previously authored Puzzle Game Level Design Handbook, v1.1** — internal prior work; core model, solver, Thread Fever-specific mechanics, role-based level design and evidence hygiene. This skill expands it with the five screw games, corrected staged-level times, detailed teaching sequences, and elapsed-time-based session planning.
- **Linked Google Sheet, gid=1357357794** — not independently accessed; specifically not used as evidence for Match Factory numbers.

Do not share third-party research tables or source recordings externally where the supplied workbooks restrict redistribution. The resulting skill is meant for internal planning and review.

---

# Appendix: Full source evidence

## Benchmark Evidence — Puzzle Level Design Skill

**Scope:** audited user-supplied Pixel Flow Deconstruction (2)(1).xlsx, Playliner_Puzzle_Games_iOS(1).xlsx, Screw Puzzle Analysis(1).xlsx and HaleTurer_CaseStudy_LevelDesigner(1).pdf (3 spreadsheets + 20-page PDF). This file documents observations, not recommendations. Counts describe the captured playtests, not population-level user outcomes. Internal use only where workbook licenses so state.

### Readme protocols and comparability

- **Pixel Flow:** one 300-level deconstruction; Level Play Time (sec) is a recorded gameplay duration. No population-level fail/retention study is included.
- **Playliner:** six games, three non-paying testers for each, iOS, 7+ hours of tests and 5+ days of recording; ad watching barred except exceptional cases after five failures. Duration_CompleteAttempt is the successful attempt; Duration_Total includes all attempts to full completion.
- **Screw Puzzle:** five games, four scripted profiles per game (two RV watchers, two non-RV), at least 5h/30 completed levels per tester; RV profiles intentionally watched ads at level starts and after failures; no-RV players watched ads only after a specified repeated-failure threshold. Comparisons of RV take-up or revenue are not natural behavior. Total durations span level-to-level starts including attempts.
- **Staged Block Jam:** workbook rows 3.1, 3.2, 3.3 are successive stages of one level 3. Stage completion and total time are summed per (tester, integer level) for the table below. Sample sizes count numbered levels, not stage rows.
- **Google Sheet gid=1357357794:** not accessible in this task. No Match Factory numbers are presented. Add its export and rerun the benchmark before making any Match Factory-specific claim.

### 1. Median successful completion time by level slice (seconds)

Values are median across observed tester-level records. Pixel Flow uses the recorded play-time field. In Playliner and Screw Puzzle, these are winning attempt times only; failed attempts are counted separately below. — means unavailable. Parentheses give observed tester-level or recorded level count in that band. Bands may end early for partially covered games.

| Game | L1–15 | L16–50 | L51–100 | L101–200 | L201–300 |
|---|---|---|---|---|---|
| Pixel Flow | 52 (15) | 72 (35) | 90 (50) | 164 (100) | 184 (100) |
| Car Match | 32 (45) | 58 (105) | 54 (150) | 97 (300) | 98 (75) |
| Magic Sort | 49 (45) | 72 (105) | 74 (150) | 89 (300) | 96 (108) |
| Hole People | 29 (45) | 67 (105) | 84 (150) | 132 (213) | — |
| Block Jam | 72 (45) | 155 (105) | 195 (150) | 169 (27) | — |
| Knit Out | 54 (45) | 136 (105) | 178 (150) | 170 (138) | — |
| Goods Puzzle | 204 (45) | 276 (105) | 351 (51) | — | — |
| Screw Away 3D | 397 (60) | 430 (67) | — | — | — |
| Screw Master 3D | 303 (60) | 363 (84) | — | — | — |
| ScrewSort 3D | 308 (60) | 310 (81) | 282 (7) | — | — |
| Woodle Screw Jam | 165 (60) | 344 (91) | 280 (9) | — | — |
| Screwdom 3D | 332 (60) | 431 (77) | — | — | — |

**Why not one cross-game duration target?** In the same L1–15 band, observed medians range from Hole People's 29 seconds to Screw Away 3D's 397 seconds. This mostly reflects fundamentally different level and monetization architecture; it is not evidence that either extreme is optimal.

### 2. Retry burden — median TOTAL time versus clean successful attempt

The left number is winning attempt p50, the right number is p50 total level dwell (seconds). The total includes retries and intervening time as recorded; it is not a continuous one-attempt duration.

| Game | L1–15: clean / total | L16–50: clean / total | L51–100: clean / total |
|---|---|---|---|
| Car Match | 32 / 43 | 58 / 83 | 54 / 83 |
| Magic Sort | 49 / 63 | 72 / 91 | 74 / 106 |
| Hole People | 29 / 38 | 67 / 87 | 84 / 123 |
| Block Jam | 72 / 114 | 155 / 243 | 195 / 278 |
| Knit Out | 54 / 62 | 136 / 163 | 178 / 236 |
| Goods Puzzle | 204 / 264 | 276 / 389 | 351 / 466 |
| Screw Away 3D | 397 / 528 | 430 / 625 | — |
| Screw Master 3D | 303 / 403 | 363 / 477 | — |
| ScrewSort 3D | 308 / 378 | 310 / 357 | 282 / 304 |
| Woodle Screw Jam | 165 / 202 | 344 / 507 | 280 / 302 |
| Screwdom 3D | 332 / 408 | 431 / 520 | — |

**Interpretation:** total time can be appreciably longer than the final successful attempt. An AI planner must simulate retries/total dwell to avoid underbudgeting sessions and incorrectly estimating ad/opportunity intervals.

### 3. Designed Hard and Very Hard cadence

**Pixel Flow:** all L1–23 Normal; first Hard 24, then Hard 28, first Very Hard 29. Across L1–300: 216 Normal (72%), 45 Hard (15%), 39 Very Hard (13%). Not a reliable estimate of natural win rates because labels are author classifications.

| Title | First recorded Hard labels | First recorded Very/Super Hard labels | Note |
|---|---|---|---|
| Car Match | 14, 24, 34, 44, 54, 64, 74, 84, 94, 104, 114, 124… | 19, 29, 39, 49, 59, 69, 79, 89, 99, 109, 119, 129… | Exact 10-level repeating labels from 14/19 |
| Magic Sort | 14, 24, 34, 44, 54, 64, 74, 84, 94, 104, 114, 124… | 19, 29, 39, 49, 59, 69, 79, 89, 99, 109, 119, 129… | Exact 10-level repeating labels from 14/19 |
| Hole People | 35, 90, 100, 108, 110, 117, 121, 122, 129, 133, 138, 144… | 80, 113, 115, 158, 168 | Labels recorded irregularly |
| Block Jam | 19, 43, 57, 72, 87, 102 | 44, 45, 53, 60, 68, 75, 83, 90, 98, 105 | Labels recorded irregularly |
| Knit Out | 24, 29, 32, 38, 44, 49, 52, 74, 78, 86, 109, 115… | 55, 60, 66, 71, 82, 90, 94, 98, 103, 112, 117, 124… | Labels recorded irregularly |
| Goods Puzzle | 6, 11, 19, 29, 39, 49, 59 | 15, 24, 35, 45, 52, 63 | Labels recorded irregularly |
| Screw Away 3D | 3, 8, 14, 19, 22, 23, 25, 26, 30, 35, 36 | 4, 11, 17 | Labels recorded irregularly |
| Screw Master 3D | — | — | No hard-label flags recorded in this sheet |
| ScrewSort 3D | — | — | No hard-label flags recorded in this sheet |
| Woodle Screw Jam | — | — | No hard-label flags recorded in this sheet |
| Screwdom 3D | 15, 20, 25, 30, 35, 40, 45 | — | Labels recorded irregularly |

Car Match and Magic Sort have Hard at 14, 24, 34, … and Very Hard at 19, 29, 39, … through their covered progression. Screwdom 3D marks Hard 15, 20, 25, 30, 35, 40, 45. These patterns are descriptive conventions, not proof those levels are measurably more difficult or optimal.

### 4. Loss-progress audit — not all fails are near-wins

The source Losses tab logs attempts and recorded fraction of the game's stated completion goal at defeat. Game definitions differ (cars, bottles, bobbins, block units, etc.); cross-game numeric values are not equivalent in felt progress, and a late timer loss after high objective completion may still feel unfair. The values below are empirical distributions among logged losses, not prescribed targets.

| Source / title | Logged losses | Median progress at loss | At ≥75% of goal | Below 50% of goal |
|---|---|---|---|---|
| Playliner: Car Match | 188 | 32% | 5.9% | 64.9% |
| Playliner: Goods Puzzle | 72 | 57% | 26.4% | 31.9% |
| Playliner: Magic Sort | 268 | 9% | 0.0% | 100.0% |
| Playliner: Knit Out | 256 | 22% | 5.1% | 74.2% |
| Playliner: Hole People | 202 | 32% | 3.5% | 80.7% |
| Playliner: Block Jam | 42 | 45% | 0.0% | 64.3% |
| Screw: Screw Away 3D | 162 | 62% | 27.8% | 23.5% |
| Screw: Screw Master 3D | 172 | 65% | 4.1% | 14.5% |
| Screw: Screw Sort 3D | 102 | 64% | 17.6% | 15.7% |
| Screw: Woodle Screw Jam | 172 | 45% | 8.7% | 57.0% |
| Screw: Screwdom 3D | 66 | 70% | 34.8% | 13.6% |

**Interpretation:** many tested losses happen well before 75% of the objective; therefore the phrase "successful games always engineer near-miss failures" is unsupported. Use near-win and fair recoverability as testable hypotheses, never as a reason to script unavoidable losses.

### 5. Ads and the risk of level-count-only pacing

First interstitial level varied by tester in the Playliner workbook. The next column sums the recorded total dwell in levels before that player's first recorded interstitial. These are cumulative progression times spread across sessions and test days, not 20–120-minute uninterrupted sessions. Reported ranges are across the three testers.

| Game | First interstitial by tester (levels) | Cumulative preceding dwell (minutes) |
|---|---|---|
| Car Match | 21, 15, 21 | 8.6–24.4 |
| Goods Puzzle | 14, 11, 6 | 18.6–49.1 |
| Magic Sort | 60, 60, 60 | 81.4–128.8 |
| Knit Out | 31, 37, 40 | 58.0–99.5 |
| Hole People | 1, 73, 45 | 0.0–109.3 |
| Block Jam | 21, 21, 21 | 41.1–53.9 |

**Other observations:** Screw Away, ScrewSort, Woodle, and Screwdom record interstitials by roughly levels 4–6 for at least some testers, whereas Screw Master varies. These values depend on experimental RV/Non-RV instructions and must not be interpreted as organic ad LTV. No source provides true session-ID data or ad-network revenue, so a 60-minute uninterrupted session monetization gap cannot be empirically confirmed from these workbooks.

### 6. Feature, booster, and reinforcement observations

**Pixel Flow — Level Blockers Overview** (level first appearance):

- **L1 — Standard Pixel:** The base destructible unit of the game. Can only be destroyed by a matching color shooter with a clear line of sight. Establishes the core rules of color matching, visibility, and outer to inner clearing logic that all other obstacles build upon.
- **L8 — Surprise Shooter:** Introduces uncertainty by hiding the shooter's color until it is deployed. Forces players to plan around incomplete information rather than deterministic sequencing.
- **L14 — Connected Shooters:** Two shooters are physically connected and move together on the conveyor. Adds multi color dependency and increases spatial planning complexity.
- **L30 — Hard Pixels:** Voxels that require multiple hits to be destroyed. Introduces durability as a new layer of constraint and shifts focus from order to resource sufficiency.
- **L40 — Locks and Keys:** Locked pixels block progression until matching keys are freed. Creates mandatory clearing order and enforces indirect path solving.
- **L55 — Door:** A movable barrier that blocks line of sight until pulled back by shooting. Introduces dynamic obstacles that must be interacted with rather than cleared.
- **L70 — Pixel Pipe:** Replaces destroyed pixels by spawning new ones from a fixed source. Introduces regeneration pressure and prevents simple linear clearing strategies.
- **L85 — Surprise Pixel:** Hides its color until front layers are cleared. Forces players to commit actions under incomplete information and delays color dependency resolution.
- **L100 — Snake:** A moving obstacle that occupies space and must be pushed back to its origin by shooting. Introduces motion based spatial control rather than static blocking.
- **L120 — Wall:** Completely blocks line of sight and cannot be destroyed. Enforces strict firing lanes and elevates spatial planning as a primary constraint.
- **L140 — Egg Box:** Contains multiple hidden pixels that must be cracked open by shooting. Adds multi stage destruction and increases shot investment per area.
- **L171 — Shooter Pipe:** Stores trapped shooters and releases them once the pipe is cleared. Introduces delayed resource recovery and rewards freeing blocked capacity.
- **L200 — Ice Blocks:** Covers pixels with a breakable layer that must be cleared before interacting with the pixel itself. Adds an extra destruction step and increases shot cost per area.
- **L230 — Wooden Blocks and Hammers:** Wooden blocks block progress and can only be destroyed by pushing hammers to the front of the queue. Introduces tool prioritization and forced queue manipulation.
- **L290 — Frozen Shooter:** A shooter encased in ice that cannot be used until surrounding ice layers are broken. Introduces dependency based activation and forces indirect progression through other shooters.

**Pixel Flow — Boosters Overview:**

- **L7 — Extra Tray Slot:** Increases the maximum number *(kaynak metin burada kesik geldi)*
