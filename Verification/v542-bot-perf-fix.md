# 5.4.2: adaptive hidden-step performance fix

4 November 2026. Sol/high technical diagnosis and implementation, using the diagnosing-bugs skill. Root retains actual WebGL verification and final acceptance in [STATUS](V542-NEW-TRACK-BOTS-STATUS.md). Source request and common-bot constraints remain those of the [core integration handoff](v542-new-track-bots-core.md). No gameplay/search budget, probability, geometry, flight parameter, UI or pause-guard change was made for performance.

## Reproduced failure and fast loop

Root observed Intercepts/Chosen/Novice seed **2944602636** in the actual WebGL candidate: repeated planning frames over 50 ms, a maximum combined frame around 324 ms, and the normal >0.3 s pause guard showing the brief-stop message. Root's [browser before snapshot](v542-browser-perf-before.json) remains authoritative for that browser observation.

The agent-runnable Release loop is:

```powershell
dotnet run --configuration Release --project Tools/NewTrackBotCheck/NewTrackBotCheck.csproj -- --perf-before
```

It drives the public controller `Advance` call site until the first two real planning calls, with fixed track/profile/difficulty/seed/warning/camera. One warm-up precedes three measured repetitions; measured maximum must be ≤50 ms. The original command returned **FAIL**, warm maxima **112.68 / 114.91 / 112.89 ms**. [Before evidence](v542-bot-perf-before.json) records exact sources, selected inputs, intentions, rolls and captured state. Minimising to only the first plan at elapsed 0.100 s also failed every measured repeat; [minimal case](v542-bot-perf-min.json) retains the JIT/timing spread. No hidden reveal boundary is reached in that first-plan horizon.

## Ranked hypotheses and causal result

Before the diagnostic control or production patch, the following ranked hypotheses and predictions were sent to root:

1. The unconditional 1/240 s shared step on any hidden course multiplies analytic work even with no Warning nearby. Removing only irrelevant Hidden flags from a copied test track, retaining both participants and trial inputs before every reveal boundary, should give a large speedup with equivalent physics.
2. Simulating the additional human body is the main cost. Keeping Hidden flags but making that participant terminal should give most of the improvement if true.
3. Forecast checkpoint cloning/allocation dominates. The measured cost of the same checkpoint count should explain most of a plan if true.
4. Repeated immutable geometry/timing lookups may contribute secondary overhead; cache work would only be justified after the larger multipliers were isolated.

[Causal control](v542-bot-perf-control.json), 32 repeated 1000-unit simulations from the actual first-plan origin x=22 to x=1022, compared the same inputs and **two-player** Lagger shells. The first hidden reveal boundary is x=1920, beyond this probe:

| Control | Mean milliseconds per evaluation | Allocated bytes per evaluation |
| --- | ---: | ---: |
| Current Hidden flags, both participants | 3.770 | 39801 |
| Only test-track Hidden flags removed, both participants retained | 0.403 | 9145 |
| Hidden flags retained, human terminal | 2.235 | 34505 |

Hidden-off was **9.35×** faster while both participants' significant physical state and world time agreed. Human-off improved only 1.69×. The actual 2215 checkpoint clones cost about 5.0 ms separately, far below the approximately 113 ms warm plan. This identified the unconditional small shared steps as the primary cause. No cache or human-state simplification was needed.

## Minimal production change

Only `Assets/Scripts/Core/HiddenObstacles.cs`, `StepLimit` and its observer comment, changed after root approved the causal proof and an independent correctness review:

- An unrevealed stone bounds the shared slice by its effective warning duration. A warning discovered inside that slice therefore cannot also expire before all participants advance and Update runs.
- An existing future Warning cuts at its exact remaining deadline.
- An overdue Warning still occupied after Update uses the original **1/240 s** body-clear sampling bound.
- Solid stones impose no additional shared step; all-solid worlds use ordinary analytic stepping.

Simply returning infinity whenever no Warning existed would have been incorrect: a newly revealed warning could expire inside the same large slice. The unrevealed-duration bound prevents this. Positive finite metadata validation and duration >1/240 s, safe activation, exact earliest observer and stable IDs are unchanged. No new scheduler, distance predictor, lookup cache or special bot/track case was introduced.

## Red → green and semantics

The same two-plan Release command with `--perf-after` returned **PASS**, measured maxima **32.46 / 32.57 / 40.75 ms**. [After evidence](v542-bot-perf-after.json) preserves all repetitions. These are managed timing results; root's actual rebuilt browser is still required for the user-visible gate.

The complete original-seed Novice race was captured [before](v542-new-track-bots-perf-race-before.json) and [after](v542-new-track-bots-perf-race-after.json). Both finished with **one hit, two full ordinary rewards, finish time 35.90 s**. Managed max/P95 planning changed from approximately **507.8 / 341.7 ms** in the earlier cold run to **47.7 / 46.8 ms** after the fix; warm-up/concurrent load affect these desktop values.

`--compare-perf` returned **PASS: 1245 assertions**. [Semantic comparison](v542-bot-perf-semantics.json) checks actual inputs, selected trial inputs, intentions, RNG rolls, probabilities, outcomes, captured physics and search counts. The complete race retained **31 plans, 2403 candidates and 100146 outer forecast advances**, with zero suppressed input. Floating tolerance is 2e-7. The final actual observer slice count changed 7188→7189 due to sub-epsilon terminal rounding under the different analytic partition; it is explicitly excluded together with timing metrics. Every actual slice still agrees with its own ordinary-world replay.

## Postfix correctness and integration checks

- Existing `Tools/HiddenObstacleCheck`: **PASS, 17 groups / 1324 assertions**.
- New contract cases span reveal and deadline in a **single** `Advance(0.317)` or `Advance(10)`, with ordinary and tiny valid authored duration **1.1/240 s**, an actual pre-reveal speed bonus, body overlap/clearance and reversed player arrays. They add 72 assertions to the existing short bot contract checks.
- [Postfix five-pair Master smoke](v542-new-track-bots-postfix-smoke.json): **PASS, 135189 assertions / five races**; all selected full effects and finish, clean expected times.
- [Postfix shared-world runtime](v542-new-track-bots-postfix-shared.json): **PASS, 29586 assertions**; the documented resumed-state 20-unit human lead triggers reveal before the bot threshold, then all motion/input is ordinary production physics. It is not presented as a fresh-start human playthrough.
- [Postfix bounded matrix](v542-new-track-bots-postfix-matrix.json): **PASS, 1245456 assertions / 46 races**, same fixed schedule as before: all levels × five new pairs, Master at all global warning settings, middle-level seed diversity, representative cadence, exact old-map data, shared snapshot/source independence, ordinary replay and original search/horizon caps. Core hashes remained unchanged during the run.

The final matrix's warm Master planning maxima were about **1.6–2.7 ms**, most lower-level maxima about **13–18 ms**, with the first colder Novice case at **47.3 ms**. These observations explain the managed improvement without claiming browser/phone timing. Root was informed immediately when the final CPU matrix completed; no more CPU tests or production writes were started during browser measurement.

The reproduction/causal controls are retained as clearly labelled test-tool modes and documentation. No temporary production logging or profiling instrumentation remains. Previous candidate reports and before artifacts remain available; current UI hash checks, actual browser performance, final build/archive and acceptance belong to root.
