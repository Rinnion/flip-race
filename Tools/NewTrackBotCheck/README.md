# New-track bot integration checks (5.4.2)

Run from the Unity repository. The projects use only the local .NET SDK and production Core source.

```powershell
dotnet run --configuration Release --project Tools/NewTrackBotCheck/NewTrackBotCheck.csproj -- --contract
dotnet run --configuration Release --project Tools/NewTrackBotCheck/NewTrackBotCheck.csproj -- --smoke
dotnet run --configuration Release --project Tools/NewTrackBotCheck/NewTrackBotCheck.csproj -- --lower
dotnet run --configuration Release --project Tools/NewTrackBotCheck/NewTrackBotCheck.csproj -- --shared
dotnet run --configuration Release --project Tools/NewTrackBotCheck/NewTrackBotCheck.csproj -- --matrix
```

Each invocation writes only its own `Verification/v542-new-track-bots-{mode}.json`. It never overwrites historical stochastic reports. Core hashes before/after guard the production freeze. Reports distinguish actual runtime inputs and replay from structural fixtures and record managed planning max/P95; those timings do not establish WebGL performance.

Every mode runs the short contracts first:

- Exact B/C/base/Master scalars, reward/obstacle opportunities and input doubles on all ten old track/profile combinations versus `AcceptedMapBaseline.cs`, the accepted 5.4.1 map copied before integration. Only its namespace and a Core import were changed for standalone compilation. This is a frozen test fixture, not another runtime bot.
- New references expose only selected compatible reward IDs, retain every logical obstacle and the shared probability formula; Rethink/Fast remains unsupported.
- Five new track/profile pairs × five global warning settings: independent full hidden forecasts keep existing timers/solid states, warning selection, active human bodies and stable player IDs. Constant-camera elimination is disabled in the forecast projection. Current human body postponement, subsequent ordinary body clearance, source/sibling independence and one real Advance are checked. These are explicitly structural initial-state fixtures.
- Structural terminal Master fixtures: incidental unselected failed rewards or extra completed pulses do not invalidate Master; missing/failed selected goals and incomplete aggregate/finish service do. This is an aggregate ledger guard, not proof of per-ID pulse completion for merged instant effects.

The runtime schedules are fixed before interpretation:

| Mode | Cases |
| --- | --- |
| `--contract` | No full bot races |
| `--smoke` (default) | Master, seed 0, global warning 0.8 s on all five new pairs |
| `--lower` | One Novice/Intercepts/Chosen, seed 123, warning 0.8 s, for early managed timing |
| `--shared` | Rethink/Chosen/Master/seed 0 with human reference inputs; explicitly resumed initial state has a 20-unit human lead, then all movement/inputs use production rules. Requires the human to reveal before the bot's own threshold. This is not a fresh-start human playthrough. |
| `--matrix` | 46 cases: four remaining levels × five pairs at seed 123/warning 0.8; Master × five pairs × four other global warning choices; Confident × five pairs with seed 456 for actual input diversity; one Confident/Intercepts/Fast seed-123 cadence comparison at 30 rather than 60 fps. Master/0.8 is already covered by smoke. |

The observer replays actual controller Press/Advance on an independently created ordinary world. Complete mutable timeline comparisons run after planning and actual bot input; elapsed advancement is compared on every observed slice. Pause must preserve source state, cursor and saved decisions. Master must finish clean, collect every selected goal, serve the aggregate ordinary count and full finish effect, and match reference time. Other levels may miss intentions and suffer physical hits; no empirical percentage band is imposed. Original horizon/overrun/candidate caps, one saved intention per logical ID, distinct actual middle-level inputs across two seeds and a representative cadence replay are checked.

Unknown future human inputs are not predicted. The forecast independently advances existing participant state with ordinary physics and emits trial Press only for the bot. The actual world remains authoritative.

## Fixed-seed performance regression and adaptive hidden steps

The real WebGL regression was reproduced at Intercepts/Chosen/Novice seed `2944602636`: planning frames stalled and could trigger the existing >0.3 s pause guard. The retained fast diagnostic modes exercise the actual public `Advance` → `Decide` → `Plan` call site:

```powershell
dotnet run --configuration Release --project Tools/NewTrackBotCheck/NewTrackBotCheck.csproj -- --perf-before
dotnet run --configuration Release --project Tools/NewTrackBotCheck/NewTrackBotCheck.csproj -- --perf-min
dotnet run --configuration Release --project Tools/NewTrackBotCheck/NewTrackBotCheck.csproj -- --perf-control
dotnet run --configuration Release --project Tools/NewTrackBotCheck/NewTrackBotCheck.csproj -- --perf-after
dotnet run --configuration Release --project Tools/NewTrackBotCheck/NewTrackBotCheck.csproj -- --perf-race-before
dotnet run --configuration Release --project Tools/NewTrackBotCheck/NewTrackBotCheck.csproj -- --perf-race-after
dotnet run --configuration Release --project Tools/NewTrackBotCheck/NewTrackBotCheck.csproj -- --compare-perf
```

The two-plan gate runs one warm-up and three measured identical fixed-seed prefixes, returning failure if a measured planning maximum exceeds 50 ms. `--perf-min` retains only the first plan. Before/after evidence is stored under distinct names and must not be regenerated on the wrong source version. The causal control removes Hidden flags **only in a copied test track**, retaining both forecast participants and the same trial inputs; it asserts the entire probe ends before any reveal boundary. The separate terminal-human control and checkpoint-clone timing narrow alternative explanations. These diagnostic modes are intentionally retained as scoped regression tools; no temporary production instrumentation remains.

The fix changes only the shared hidden step limit. Unrevealed stones cap a slice by their effective warning duration, future warnings cut at the exact deadline, overdue occupied warnings retain the 1/240 s clearance sampling bound, and all-solid worlds use ordinary analytic stepping. A new warning therefore cannot also expire inside the same slice. Validation, safe activation, controls, probabilities, search horizons/budgets and pause guard stay unchanged.

All contract modes now also check single `Advance(0.317)` and `Advance(10)` calls spanning reveal/deadline, an authored duration of 1.1/240 s, a real pre-reveal boost, active-body postponement/clearance and reversed player arrays. The existing HiddenObstacleCheck retains its 1324 assertions. `--postfix-smoke`, `--postfix-shared` and `--postfix-matrix` repeat the declared schedules under separate output filenames, preserving the earlier candidate snapshot. `--compare-perf` compares actual/selected inputs, intentions, RNG rolls, probabilities, outcomes, search counts and captured physical state with tolerance 2e-7. Timing metrics and a final sub-epsilon observer-slice count are excluded; the ordinary replay still checks each actual Advance against its own independent world.
