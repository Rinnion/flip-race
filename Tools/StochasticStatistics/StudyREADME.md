# Named training studies

Working tooling only. Historical initial DLL, initial manifest and reports remain immutable.
The original Publish.ps1 is historical initial-publication tooling and must not be reused for named studies.

Each study is a reviewed config in Studies/<studyId>.json. V1 accepts Training only and rejects every seed >=1000000.
No Validation/hold-out mode exists. A future validation protocol needs explicit phase-aware summary metadata and its own review before execution.

Configuration selects a Cartesian subset of tracks, profiles and levels. Each selected pair supplies exactly one positive finite k.
pCollect=q; pHit=k*(B/C)*(1-q); Master remains1/0. Unsupported probabilities are rejected without clamping.
Nonoverlapping seed ranges expand into ascending seeds; N>=200 per cohort. Canonical schedule order is track, profile, level, seed.
Input config order does not change the schedule/table hash; raw config bytes have a separate hash.
Master-only studies still require an explicit pair k, which is unused for Master.

Preparation commands (no physical races):

    dotnet build Tools/StochasticStatistics/StochasticStatistics.csproj -c Release --no-restore
    dotnet Tools/StochasticStatistics/bin/Release/net10.0/StochasticStatistics.dll --self-check
    dotnet Tools/StochasticStatistics/bin/Release/net10.0/StochasticStatistics.dll --self-check-study
    dotnet Tools/StochasticStatistics/bin/Release/net10.0/StochasticStatistics.dll --describe-study Tools/StochasticStatistics/Studies/postclip-initial.json

Publish only after the coordinator's explicit GO:

    ./Tools/StochasticStatistics/PublishStudy.ps1 -ConfigPath ./Tools/StochasticStatistics/Studies/<id>.json

The publisher snapshots all Core/*.cs and this tool's top-level sources/protocol/schema/docs plus the exact used config.
It checks source hashes before/after compilation, publishes a fresh Release DLL, exact canonical table/schedule,
and byte-identical Manifests/<id>.json. Published files become read-only. Existing study manifest/output/claim prevents reuse.
The original InitialPublicationManifest.json and all initial artifacts are never overwritten.

After published hashes and published DLL selfchecks are accepted, run only on a separate GO:

    dotnet "<publication>/StochasticStatistics.dll" --study --study-id <id> --parallelism 3

Runtime reads only frozen manifest/config beside that DLL, verifies all published hashes, and permits no seed/parameter overrides.
It uses the same Run sampler as initial training with explicit CreateFixed settings and declared camera.
All outcomes/errors remain; bot terminal stops that case while human may remain racing. Terminal quality failures enter aggregates.
Partial counts remain raw but not terminal statistics. Missing terminal rows null the relevant cohort mean/CI.
Student-t conservative2.601 applies to N>=200. All intervals are exploratory, not validation acceptance.

Outputs are fresh Verification/stochastic-bots-study-<id>.json/.txt with all cases sorted by canonical index,
per-cohort mean/sample SD/99% CI, intentions/facts/unreachable counts, hashes and all unrun IDs.
A permanent ignored Recovery/<id>.claim prevents accidental reruns; recovery manifests preserve observed cases after postrun failures.
If manifest/hash verification fails after sampling, publicationStable=false and observed/unrun outcomes are retained.
A pre-worker setup failure records zero actual cases and known schedule/unrun IDs when available.

recordSelectedInputs defaults false. If true, arrays are saved initial decision combos, not applied Press tapes.
The separate existing Risk/World observer proofs cover actual action replay. This sampler does not pretend to repeat that audit.
Current approved configs are preparation artifacts; their presence is not publication or cohort execution authorization.


Suppressed inputs are separate telemetry: raw cases expose suppressedInputs from observer Kind="suppressed".
A suppression performs no ordinary Press, so Accepted=false on that observer event does not count as a rejected Press.
Only Kind="press" increments acceptedInputs/rejectedInputs. Terminal summaries include suppressedInputs independently;
raw partial counts are retained, while any nonterminal observation makes the terminal suppression mean/CI null.
Real rejected Press remains a separate strict failure in validation. No suppression is relabelled as accepted input.
Metadata/synthetic selfchecks exercise mixed event kinds and terminal-quality/partial aggregation without running a race.
Historical published DLLs/reports keep their original schema and bytes.