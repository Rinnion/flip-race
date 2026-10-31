# Initial stochastic-bot training sampler

Ownership: this tool and `Verification/stochastic-bots-training-initial.json/.txt` only. No Core/UI edits. Contract BSTO02/05 and `Verification/V5-STOCHASTIC-BOTS-STATUS.md` govern integration. API compilation/publication waits for the settings/factory owner freeze; the 10,000-case run additionally requires root readiness.

The fixed protocol is in `Protocol.json`: five full tracks × Chosen/FastFlip × five difficulties, exactly seeds0..199 each, Lagger, human0 idle. Prior exploratory0..11 are training. Held-out1000000..1000199 are reserved and this CLI refuses them. Only the initial model is supported; there is no calibration selection or parameters override.

After root's API freeze, compile from Unity root:

```powershell
dotnet build Tools/StochasticStatistics/StochasticStatistics.csproj -c Release
```

The compiled DLL also supports `--self-check` (analytic aggregation/missing-data checks and initial50 parameter-schema checks only; no race) and `--describe-initial` (metadata only).

After publication readiness:

```powershell
pwsh -NoProfile -File Tools/StochasticStatistics/Publish.ps1
```

The publisher does not simulate a race. It creates a unique `Published/initial-...` Release binary, snapshots Core/tool/protocol sources, verifies unchanged source hashes, and asks that exact DLL to describe all50 immutable initial settings. It writes `training-manifest.json` beside the DLL and the same bytes to tracked `InitialPublicationManifest.json`, then marks published files readonly. `bin/`, `obj/`, and `Published/` are ignored. No existing publication or tracked initial manifest is replaced.

After root explicitly authorizes the declared training cohort, invoke the exact DLL path printed by publication:

```powershell
dotnet "Tools/StochasticStatistics/Published/initial-.../StochasticStatistics.dll" --training --start-seed 0 --count 200 --parallelism 3
```

Run from Unity root. The sampler verifies assembly, initial parameter table and every published file against the frozen manifest before races; it verifies that publication again afterward. It never recompiles or substitutes current working-tree source. Parallelism is restricted to1..3 CPU workers; final cases are obligatorily sorted by the fixed track/profile/level/seed order. The exact10000-case schedule is hashed before cohort and checked against the immutable publication; physical worker completion order may differ, never selection or final seed order.

Each case uses actual `StochasticBotSettings.CreateInitial`, `RaceSimulation.CreateStochasticBotRace`, ordinary human ready/start Press, and the settings-reading controller on bot slot1. Ready replaces players, so the current bot is resolved afterward. Only controller Press/Advance runs the race; it stops at bot terminal while the human may still be racing. A simple observer counts emitted input acceptance; there is no expensive full-slice state replay here. Dedicated Risk/World checks cover replay separately.

Every case preserves status, actual terminal time/distance, errors, raw success/contact/failure/hit IDs, effect queue/served counts, full finish pulse, fixed goal completion, saved intention/roll/probability and its independently derived actual outcome. No failed/eliminated case is discarded or rerolled. Errors do not stop the remaining announced cases. Fatal interruption emits completed results plus all unrun case IDs. Systemic setup failure before any case writes a complete unrun manifest in this tool directory. Master invariant failures remain explicit even when a mean of known actual times can be computed.

Cohort summaries include means, sample standard deviation and exploratory two-sided99% Student-t intervals for actual times and counts. With fixed n=200, t=2.601 is rounded upward for199 degrees of freedom. Terminal count metrics require a physically terminal participant: raw partial fields remain in case records but cannot substitute complete endcounts. Terminal quality failures remain included. A metric with any missing/nonfinite observation has null aggregates/CI rather than silently dropping the case. All resolved/unresolved intention outcomes remain in the rate denominator. These training intervals are not validation acceptance, multiple-comparison guarantees, or WebGL/performance evidence. Formula reference: [NIST confidence limits for the mean](https://www.itl.nist.gov/div898/handbook/eda/section3/eda352.htm).

JSON schema: top-level publication/assembly/parameter hashes, `publicationStable`, declared schedule counts/unrun IDs, `cohorts[50]` with initial parameters/metric summaries/intent-versus-actual counts, and sorted `cases[10000]` with raw terminal/error facts. Existing initial outputs are preserved: the CLI refuses overwriting them. Ctrl+C requests cancellation, retains started case errors and explicitly lists all remaining seeds without retry.
Post-run provenance errors retain accumulated outcomes: missing/changed manifest or published-file hashes set publicationStable=false and fatalError in the normal report. An unexpected later exception uses an ignored Recovery/ report with actual completed cases and remaining IDs; SETUP_FAILED_NO_COHORT applies only before a worker begins. The self-check injects a synthetic missing-manifest exception after two synthetic observations and proves both survive with one unrun ID. This fix applies to the working tool only; the initial published DLL and its source snapshot remain unchanged.


Suppressed inputs are separate telemetry: raw cases expose suppressedInputs from observer Kind="suppressed".
A suppression performs no ordinary Press, so Accepted=false on that observer event does not count as a rejected Press.
Only Kind="press" increments acceptedInputs/rejectedInputs. Terminal summaries include suppressedInputs independently;
raw partial counts are retained, while any nonterminal observation makes the terminal suppression mean/CI null.
Real rejected Press remains a separate strict failure in validation. No suppression is relabelled as accepted input.
Metadata/synthetic selfchecks exercise mixed event kinds and terminal-quality/partial aggregation without running a race.
Historical published DLLs/reports keep their original schema and bytes.