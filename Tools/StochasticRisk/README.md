# Stochastic risk prototype

Contract: `flip-race/docs/flip-race-stochastic-bots-delivery-2026-10-31.md`, BSTO00/03/04. Status: `Verification/V5-STOCHASTIC-BOTS-STATUS.md`.

Prepare against the root-owned controller API; do not claim a run before the API/source freeze exists. Run from `flip-race-unity` after the integration owner publishes the horizon/candidate caps:

```powershell
dotnet run --project Tools/StochasticRisk/StochasticRisk.csproj -- --smoke --max-horizon=CAP --max-candidates=CAP
dotnet run --project Tools/StochasticRisk/StochasticRisk.csproj -- --max-horizon=CAP --max-candidates=CAP
```

Replace CAP with the actual published numeric limits. Omitting a limit leaves that budget explicitly unverified in the report. `--max-horizon` is the maximum distance horizon; `--max-candidates` is candidates per planning call. The telemetry is CPU managed-run cost, not a WebGL or physical phone performance claim.

Smoke announces seed0 on Cascade/Mixed × Chosen/FastFlip × q .5/.75/1 in Lagger, plus q1 in Constant:16 cases,30fps. Full announces seeds0..11 in all those configurations with30fps/60fps/irregular cadence:576 cases. No per-run old accuracy bands, survivor filtering, seed retries or hidden success quotas. The first concrete defect stops execution and writes the failing case and explicit unrun schedule. The fixed schedule is recorded before interpreting outcomes.

Every actual observer Press/Advance is replayed on an independently created ordinary Solo participant. All public personal state fields, nested effects, FIFO queues, sets and events are compared immediately, together with world phase/time/countdown/camera. Local forecasts never advance this ordinary player. Saved logical IDs, intent/roll/probability/input commitments cannot vanish or reroll. Pause/invalid deltas and terminal state cannot advance the world, input cursor or decisions. Full mode compares the same seed across cadences. Master must finish clean with all positive effects and a full finish dash in both cameras, matching the test-only compatible Probe.Master reference.

Reports: `Verification/stochastic-bots-risk.json` and `.txt`. They retain actual outcomes, intention failures, selected inputs, planning calls/candidates/forecast steps/horizon, observed timing cost and branch witnesses. LastHit/LastSuccess/LastFinishBoost provide an exact timestamp only for their matching ID. Other set transitions have observed intervals; endpoint distance is not presented as exact contact distance. Pending-to-failed and untouched-linked-reward-after-hit witnesses are distinguished from cases where a pending interval was never observed. These logs are evidence, not an invented event sequence.

This tool owns no production files, UI, build or historical verification reports. It does not establish full50-combination statistics or native performance acceptance.
Targeted cadence regression, only after the controller owner freezes the FPS fix:

```powershell
dotnet run --project Tools/StochasticRisk/StochasticRisk.csproj -- --repro-cadence-seed4 --max-horizon=1000 --max-candidates=96
```

This separately announces Cascade/Chosen/q=.5/seed4/Lagger at30fps,60fps and irregular cadence, retains the exact full comparison, and writes only `Verification/stochastic-bots-risk-repro-seed4.json/.txt`. It does not change the default576-case schedule or smoke16. Targeted and smoke flags cannot be combined. Preserve exact reports before any subsequent run overwrites their own output names; do not relabel targeted evidence as full-matrix coverage.
## Requested versus actual forecast extent

The next engineering-stage API distinguishes `MaximumRequestedHorizon` (planned end minus origin), `MaximumTrialHorizon` (largest physical distance extent observed after every forecast Advance, including discarded trials) and `MaximumTrialOvershoot` (largest pointwise excess over that trial own requested end). `ForecastObservations` must equal `ForecastSteps`, with `ForecastHorizonViolations==0`. The old requested-only `MaximumObservedHorizon` is never presented as actual trial reach.

Default engineering bounds are requested1000, actual1055 and pointwise overrun55 world units. The overrun is one maximum forecast slice .125s × maximum speed440. Every comparison allows only explicit floating tolerance2e-7 units. This permits a discarded forecast step to cross the requested end before complete rollback and excludes its score/events; it does not permit a larger selected plan, live state mutation or an unbounded trial. FPS/replay/candidate checks are unchanged.

After the controller owner freezes the new counters/Master patch, commands are:

```powershell
dotnet run --project Tools/StochasticRisk/StochasticRisk.csproj -- --repro-cadence-seed4 --max-horizon=1000 --max-trial-horizon=1055 --max-trial-overrun=55 --max-candidates=96
dotnet run --project Tools/StochasticRisk/StochasticRisk.csproj -- --smoke --max-horizon=1000 --max-trial-horizon=1055 --max-trial-overrun=55 --max-candidates=96
dotnet run --project Tools/StochasticRisk/StochasticRisk.csproj -- --max-horizon=1000 --max-trial-horizon=1055 --max-trial-overrun=55 --max-candidates=96
```

Preserve exact reports before each output stem is overwritten. These engineering bounds do not turn the managed risk CPU timings into native WebGL performance evidence.
Safe output selection: append --output-stem=Verification/stochastic-bots-risk-clippingfix-smoke (or another explicit new name). Only scoped lowercase risk filenames are accepted; any existing JSON or TXT causes refusal before races. This preserves accepted evidence rather than silently overwriting the default stem. Limits, schedule and all assertions are unchanged.
