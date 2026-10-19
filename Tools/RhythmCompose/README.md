# Offline rhythm module authoring

This tool uses production `Press`/`Advance` and the deep snapshot scheduler from `TrackProofControl`. It does not change Assets or implement another physics model. The original beam search and its earlier candidate file remain available as development evidence; a candidate alone is not a verified route or recovery guarantee.

Run from the Unity project root:

```powershell
dotnet run --project Tools/RhythmCompose/RhythmCompose.csproj -c Release -- --refine-targets Verification/rhythm-module-refined-candidates.json
dotnet run --project Tools/RhythmCompose/RhythmCompose.csproj -c Release -- --export-modules Verification/rhythm-modules.json
dotnet run --project Tools/RhythmCompose/RhythmCompose.csproj -c Release -- --export-modules Verification/rhythm-modules-compact.json --compact
dotnet run --project Tools/RhythmCompose/RhythmCompose.csproj -c Release -- --chain-subsets
```

The later density revision adds a mandatory mirrored gate after the before-stone. It preserves earlier exports and is selected explicitly:

```powershell
dotnet run --project Tools/RhythmCompose/RhythmCompose.csproj -c Release -- --refine-targets Verification/rhythm-module-refined-before-candidates.json
dotnet run --project Tools/RhythmCompose/RhythmCompose.csproj -c Release -- --export-modules Verification/rhythm-modules-before.json --compact --before-refined
```

This changes before to endX520/exit floor and2 Fast/4 Main actions. Without `--before-refined`, export retains the original before module and its original seed file.

The later real recovery failure at gate330 is retained as a RED. The corrected candidate uses gate400/end590 and preserves historical JSON files:

```powershell
dotnet run --project Tools/RhythmCompose/RhythmCompose.csproj -c Release -- --refine-targets Verification/rhythm-module-before400-candidates.json --before400
dotnet run --project Tools/RhythmCompose/RhythmCompose.csproj -c Release -- --export-modules Verification/rhythm-modules-before400.json --compact --before400 --large580
```

The same four large-module routes independently pass with end580 (`--large580`). The experimental `--gate250` endpoint is retained for provenance but is not selected for the course. Nominal endpoint/window proofs never substitute for the separate real-error recovery audit.

The R6 recovery candidates shift the before exit gate from400 to420 (end610), and the chain's second gate/ring by30 (gate450/ring484, end720). Earlier exports are retained:

```powershell
dotnet run --project Tools/RhythmCompose -c Release -- --export-modules Verification/rhythm-modules-r6.json --compact --before400 --large580 --r6-shifts
dotnet run --project Tools/RhythmCompose -c Release -- --chain-subsets --r6-shifts
```

This export passes71,493 assertions /5,731 actual module runs. The four routes for each changed module move only the corresponding second manoeuvre by the same geometric offset. Both mirrors, six dt, individual safe ±60 ms/full ±25 ms, actual wall exit and empty effects are checked. The existing course-level Fast after-safe second input340 remains a composer override of the historical module seed217; the two R6 recovery changes do not alter that approved choice. Full-course recovery and composition still require their own R6 checks.

The R6 chain-subset replay separately passes1,872 assertions /96 actual runs: neither reward, first only, second only, or both; two profiles, both mirrors, six dt and complete earned pulses at the endpoint. The conservative latest work bound is704 < end720. Before remains457 < end610.

Choose new output paths to retain previous results. The export reads the original seeds from `Verification/rhythm-module-author-search.json` and the revised after/chain seeds from `Verification/rhythm-module-refined-candidates.json`. The default beam-search command writes the original author-search filename; do not run it over retained evidence accidentally.

Each export object contains `kind`, `length`, `endX`, `endSide`, actual `obstacles`/`zones`, conservative `latestAllWorkEndX`, and four routes. Route fields include `profile`, `full`, distance `inputs`, `actualTimes`, finite-grid `windowsSeconds`, input state samples and omission outcomes. All modules enter on the floor at speed220 with an empty effect queue. The stated endX is the module composition boundary, not a finish line. The isolated test track extends another20 units to keep the boundary distinct from finish.

Production replay verifies both mirrors, two profiles and six time steps, plus each safe input ±60 ms and each full input ±25 ms. Fast inputs must use real support. Mirrors begin on a ceiling reached through real input, not substituted player state. The output boundary requires actual wall support and an empty, completed effect ledger. An input omission is judged against the complete module contract, including its exit wall; some return inputs serve only composition, so usefulness must be checked again after the complete course is assembled.

The low/high finite windows are independent perturbations of one input with subsequent inputs still triggered by distance. A value150 ms means the search cap, not the maximum. The all-late work bound charges198 distance units to every possible ordinary reward, including linked release after the complete physical obstacle. It is independent of the chosen route. Every copied module instance must namespace its obstacle/ring IDs and remap each zone's `ObstacleId`.

See [RHYTHM-MODULES](../../Verification/RHYTHM-MODULES.md) for results and limits. Full-course recovery, two negative zones, finish-plane entry and all useful-input checks are separate composition proofs.
