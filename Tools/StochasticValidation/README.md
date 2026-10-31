# Frozen validation harness

Only this new tool is owned here. Core and linked StochasticStatistics sources remain unchanged.
net10.0; StartupObject=ValidationEntry. The shared sampler Run/Stats is linked, not copied.
No publication or physical held-out execution is authorized by this preparation.

Metadata build/selfcheck:

    dotnet build Tools/StochasticValidation/StochasticValidation.csproj -c Release
    dotnet Tools/StochasticValidation/bin/Release/net10.0/StochasticValidation.dll --self-check-validation Verification/stochastic-bots-holdout-preplan-v2.json

Selfcheck uses actual difficulty factory ONLY at seed0 and snapshot isolation. It never Presses, Advances or constructs a controller.
Held-out numbers appearing in synthetic schedule rows are metadata, not executed seeds.
The fixed preplan must match SHA256 2233FBCDF0B7E65D5AD1EFD864A74988B00BFC032B4CCDF7AF146F197FE7319D.
Validation is 18000 Lagger cases, ConstantAudit is10000 paired camera cases. Blocks are immutable:
first200 for all50, next200 for N400/N800, next400 for N800. No inspecting/stopping or modifying eligibility between blocks.

ConfigSchema.json describes fresh Configs/<id>.json (not created until final calibration/source/table freeze).
Only studyId, exact phase/camera, fixed preplan hash and optional recordSelectedInputs are accepted.
No Training command, subsets, seed/count/range/k/probability overrides. No automatic extension or refit.

Before final holdout: root approves config/source/table, independent technical review, explicit publication GO:

    ./Tools/StochasticValidation/Publish.ps1 -ConfigPath ./Tools/StochasticValidation/Configs/<id>.json

Publisher snapshots all Core C#, both linked Statistics C# files, this tool/protocol/config/preplan and unique Release DLL.
Exact canonical table/schedule/config/source/assembly hashes are frozen, manifest byte-copied to Manifests/<id>.json,
publication files made read-only. Existing manifest/output/permanent claim rejects repeats.
A second permanent CreateNew claim is keyed by preplan SHA256 + phase, independently of study ID.
Changing the study ID cannot reopen inspected seeds; ConstantAudit has its own phase key.
Actual settings are resolved from the same difficulty factory as UI, for each exact seed. Metadata table uses actual factory seed0.
pCollect=q and Master1/0 are verified; shared positive finite junior k is inferred/checked per pair, never overridden or clamped.

After publication hashes/selfcheck and separate physical-run GO:

    dotnet "<published>/StochasticValidation.dll" --validation --study-id <id> --parallelism 3

All cases use unchanged actual Run sampler: actual factory metadata, normal human ready Press, bot slot1 controller,
human idle, ordinary Press/Advance; stop when bot terminal. No state injection, selection, rerolls or quotas.
Outputs CreateNew Verification/stochastic-bots-validation-<id>.json/.txt. Failure manifests/claims stay in ignored Recovery.
All observed rows, terminal quality failures, errors and unrun IDs survive postrun hash/read failures.
Partial metrics remain raw; they never contribute as completed terminal observations.

Reports are phase-aware with no Training/exploratory flags.
Validation uses pointwise99% CI with conservative t2.601 for N>=200, sample SD n-1; missing observations null aggregate/CI.
abs(mean-target)+half<=.2 plus explicit1e-9 numeric tolerance; overlapping boundary is INCONCLUSIVE, not PASS.
Adjacent difficulty means strictly decrease. Master clean/full gain must hold.
Any rejected input prevents PASS in either phase; metadata/synthetic fixtures check both.
Junior exact physical patterns<2 => NEEDS_REVIEW, with counts and saved intention patterns; no per-race quota.
ConstantAudit retains all eliminations/errors without survivor filtering; it applies no Lagger target test to junior cohorts.
Its first held-out seeds are paired with Lagger, so no cross-camera independence claim.


Suppressed inputs are separate telemetry: raw cases expose suppressedInputs from observer Kind="suppressed".
A suppression performs no ordinary Press, so Accepted=false on that observer event does not count as a rejected Press.
Only Kind="press" increments acceptedInputs/rejectedInputs. Terminal summaries include suppressedInputs independently;
raw partial counts are retained, while any nonterminal observation makes the terminal suppression mean/CI null.
Real rejected Press remains a separate strict failure in validation. No suppression is relabelled as accepted input.
Metadata/synthetic selfchecks exercise mixed event kinds and terminal-quality/partial aggregation without running a race.
Historical published DLLs/reports keep their original schema and bytes.