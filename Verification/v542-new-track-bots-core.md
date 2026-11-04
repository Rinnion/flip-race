# 5.4.2: new-track bot core integration

This is the pre-performance-fix candidate snapshot. Subsequent real WebGL feedback, the narrowly scoped adaptive StepLimit fix and current postfix checks are recorded in [the performance-fix handoff](v542-bot-perf-fix.md); the evidence below is retained as the earlier source snapshot.

4 November 2026. Implementation and targeted verification: Sol/high. Source request: the user asked to add bots to the three new tracks, including «Передумай», while retaining the common algorithm. Root owns [delivery STATUS](V542-NEW-TRACK-BOTS-STATUS.md), integration, actual WebGL performance, release and current product documentation. Accepted starting code: Unity 5.4.1, root revision `755d665` (implementation `484779b`).

## Changed files and boundaries

- `Assets/Scripts/Core/StochasticTrackMap.cs`: public `SupportsTrack(string id)` is an eight-ID catalogue predicate using constants only. New track/profile pairs consume the independently authored `BotReferenceRoutes` descriptor. Only its compatible raw reward IDs become collect/skip opportunities and contribute reference B. Every logical obstacle, including hidden stones and both physical bands of a gate, remains represented in C. Other reward geometry remains physically present. The old five-map reference path is retained.
- `Assets/Scripts/Core/StochasticBotController.cs`: hidden-course forecasts retain independent cloned participant bodies, existing hidden phases/timestamps and selected global warning duration. The bot is projected into slot 0 while stable player IDs remain unchanged. A Lagger shell continues excluding Constant-camera elimination inside forecasts. Other participants evolve through ordinary physics with no new trial human Press inputs. Old non-hidden maps keep their previous personal projection.
- Master assessment uses the selected reward set. Incidental alternative failures or extra completed pulses do not invalidate it. Selected goals must succeed; the ordinary completed-pulse count is at least the selected ordinary count, and finish service must be full. This is an **aggregate ledger guard**, not per-ID completion accounting for merged instant effects.
- `Tools/NewTrackBotCheck/`: new offline target runner, README and frozen accepted map fixture.
- This report and `v542-new-track-bots-{smoke,lower,shared,matrix}.json`.

`StochasticBotSettings.cs`, `RaceSimulation.cs`, `RaceSnapshot.cs`, flight parameters, track geometry, probability formula, difficulty levels, seed/hash logic, ordinary Press/Advance execution and search budgets were not changed by this specialist. The reference specialist owns `BotReferenceRoutes.cs/.meta` and its independent proof. No Unity build or commit was made here.

The factory needs no additional warning-duration parameter: map/settings creation and controller construction do not simulate or read the race warning clock. Existing Apply/Retry configure the chosen global warning before the first Advance/Plan. Targeted snapshots cover all five global settings; Rethink retains its authored 0.35 s durations.

## Verified new references and actual Master results

One fixed full-served compatible reference per pair is used; no partial-reference B, additional branch search or run library was introduced. The separately owned [reference handoff](v542-new-bot-reference-handoff.md) records preparation and compatibility limitations.

| Track/profile | Selected ordinary goals | B, seconds | Actual clean Master time, seconds |
| --- | ---: | ---: | ---: |
| Intercepts / Chosen | 8 | 2.65 | 33.35 |
| Intercepts / FastFlip | 10 | 3.25 | 32.75 |
| Manifestation / Chosen | 6 | 2.05 | 33.95 |
| Manifestation / FastFlip | 6 | 2.05 | 33.95 |
| Rethink / exact Chosen | 3 | 1.15 | 34.85 |

All include the separate finish goal and full 0.25 s finish benefit. Rethink/FastFlip remains explicitly unsupported. Every fixed difficulty still uses `collect=q` and `hit=(B/C)*(1-q)`; the actual outcome is not forced to match the intention.

## Executed targeted checks

From the Unity project:

| Command mode in `Tools/NewTrackBotCheck/NewTrackBotCheck.csproj` | Result/evidence |
| --- | --- |
| `dotnet run --project ... -- --smoke` | **PASS: 135104 assertions / five actual Master races**, seed 0, global warning 0.8; [smoke](v542-new-track-bots-smoke.json) |
| `dotnet run --configuration Release --project ... -- --lower` | **PASS: 38454 assertions / one Novice race**, Intercepts/Chosen, seed 123, warning 0.8; [early timing](v542-new-track-bots-lower.json) |
| `dotnet run --configuration Release --project ... -- --shared` | **PASS: 29513 assertions / one shared Master race**; [shared-world case](v542-new-track-bots-shared.json) |
| `dotnet run --configuration Release --project ... -- --matrix` | **PASS: 1245384 assertions / 46 actual races**; [bounded matrix and production hashes](v542-new-track-bots-matrix.json) |

The 46-case matrix covers the four remaining difficulties × five pairs at seed 123/warning 0.8; Master × all five pairs × the four other global settings; Confident × all pairs at seed 456; and one Confident/Intercepts/Fast seed-123 cadence comparison at 30 instead of 60 fps. Together with smoke, all five levels × five pairs at 0.8 and Master at all five global settings are covered. Each middle-level pair produced distinct actual input sequences across seeds 123/456. The cadence case preserved actual input distances and saved intent/roll/outcome records. These are fixed finite checks, not statistical estimates or seed retries.

Actual controller Press/Advance events are replayed into an independent ordinary world. Complete mutable timelines are compared after planning and bot input; elapsed advancement is compared on every observed slice. Pause preserves source world, input cursor and saved decisions. Requested/actual/overrun bounds remain 1000/1055/55 units; candidate limits remain 13 for Master and 96 otherwise. Every forecast step has an observation and no horizon violation occurred.

Short structural checks precede each mode. They compare exact old-map B/C/base/Master scalars, every opportunity and every input double across ten old pairs against `AcceptedMapBaseline.cs`, the pre-change accepted map with only its namespace/import adapted for compilation. No historical verification report is rewritten. They also cover all five new pairs × five warnings with a human body inside an overdue warning, existing Solid preservation, independent source/sibling states, stable player IDs, subsequent normal body clearance and one actual live Advance.

The separate shared runtime case starts from an **explicitly structural resumed state with a 20-unit human lead**. All subsequent human reference inputs, bot inputs and movement use production rules. The human actually triggers reveal while the bot is still before its own threshold; the Master remains clean with full selected effects, and actual/replay states agree. This does not claim a fresh-start human playthrough or demonstrate how that lead was created.

## Managed timing and remaining acceptance

No production change followed the runtime smoke; the matrix verifies identical Core source hashes before/after. Production remained frozen while root built the WebGL candidate. No further CPU tests were launched after reporting matrix completion, leaving root's browser timing runs free of this runner's load.

The first Release Novice timing smoke observed max/P95 planning around 429/335 ms. In the longer matrix the initial cold Novice sample reached about 538/342 ms; later lower-level maxima were about 139–212 ms, and warm Master maxima about 19–30 ms. These are managed desktop wall-clock observations, affected by warm-up and concurrent build load. They are an explicit reason to inspect actual WebGL frame cost, not proof of native frame-budget acceptance. Search budgets and behavior were not changed to improve these measurements.

Root retains actual WebGL/phone performance, menus, broader legacy regressions, build/archive checks and final acceptance. Unknown future human input remains forecast uncertainty; current shared state and existing motion are preserved independently. Master completion is validated for selected compatible goals and aggregate pulse service; the report does not invent per-reward service data or a global optimality proof.
