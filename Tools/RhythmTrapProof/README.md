# Optional negative ring near a compact gate

The latest author/runtime snapshot is R7. Use the R7 commands below for current integration; R5/R6 exports and candidate studies are preserved as historical evidence.

This offline production-Core proof supplies a local candidate for the course builder. It does not mutate runtime tracks. The first band is anchored at X1000; width16, offset112, GapTop110/GapBottom130. The negative ring is X1080, Y200, radius8 for entry from the floor, mirrored to Y40 for ceiling entry.

```powershell
dotnet run --project Tools/RhythmTrapProof/RhythmTrapProof.csproj -c Release
dotnet run --project Tools/RhythmTrapProof/RhythmTrapProof.csproj -c Release -- --alternative
```

Both profiles and mirrors use actual initial wall setup, accepted inputs, production negative pickup, complete0.6 s pulse and an actual stable opposite wall atX1190. Six dt values are tested. The normal bare-gate routes avoid the ring even when each input is individually shifted ±60 ms.

The original X1065 candidate is rejected: Fast's normal first press+60 ms collected its negative ring (zero hits, SlowsEarned1), violating the safe window. The candidate was moved15 units later without changing physics or collision rules. The successful local result is [rhythm-trap-local.json](../../Verification/rhythm-trap-local.json), [stdout](../../Verification/rhythm-trap-local-v2.txt):33 079 assertions /5 467 games. Fast's optional negative route presses atX1044, with a separately verified ±75 ms input window.

Main initially had a shorter optional interval atX1050/1066. A separate search requires a real interval of at least150 ms and finds X1046/1070, actual interval159.172 ms due to the collected slow, both inputs individually±50 ms. [Alternative stdout](../../Verification/rhythm-trap-main-alternative-v2.txt):11 685 assertions /1 861 games. The X1035/1068 proposal passes nominally but has less than5 ms local window and is not selected; its initial output is retained.

The latest geometric negative contact is firstX+102; one complete slow adds66 world units of effect work, so it ends by firstX+168, before the module boundary firstX+190. These local checks do not establish a complete course trap route or nine ordinary rewards/finish. The actual authored course, two negative placements, surrounding modules, finish and conservative all-subset tail still require end-to-end verification. Windows are individual perturbations, not simultaneous jitter boxes.

The course scope reads the composer's JSON, places the same candidate at a middle and the last bare gate, and replays complete safe/full/negative arrays with actual clock perturbations. `--runtime` additionally asserts that production Draft geometry, positive/negative fields and all route arrays match the author export:

```powershell
dotnet run --project Tools/RhythmTrapProof/RhythmTrapProof.csproj -c Release -- --courses Verification/rhythm-course-r2.json Verification/rhythm-course-r2-runtime-traps.json --runtime
```

Use the matching new course JSON and a distinct output file after revision; the R2 files are checkpoints. Results and rejected candidates are recorded in [RHYTHM-NEGATIVE-PROTOTYPES](../../Verification/RHYTHM-NEGATIVE-PROTOTYPES.md).

The final R5 runtime proof and browser export use:

```powershell
dotnet run --project Tools/RhythmTrapProof/RhythmTrapProof.csproj -c Release -- --courses Verification/rhythm-course-r5.json Verification/rhythm-course-r5-runtime-traps.json --runtime --browser Verification/rhythm-final-routes.json
```

See [RHYTHM-FINAL-ENGINE](../../Verification/RHYTHM-FINAL-ENGINE.md) for the exact final scope and hashes. Earlier R2/R4 data remains historical and may intentionally fail source-equality after the R5 geometry change.

## Recovery investigation after the R5 audit

R5 is currently a frozen candidate while recovery findings are evaluated. The following tool-only scope replays 177 reported actual impact prefixes and checks a larger control catalogue. It leaves production tracks unchanged. A temporary 30-unit suffix shift is used only for the 72 bare-gate airborne cases; wall and before-exit cases retain their original geometry.

```powershell
dotnet run --project Tools/RhythmTrapProof -c Release -- --recovery-catalogue
```

The result, exact witness schema, scope exclusions, timing limitations and source hashes are recorded in [RHYTHM-RECOVERY-CLASSIFICATION](../../Verification/RHYTHM-RECOVERY-CLASSIFICATION.md). `--recovery-classes` is the finite representative search, and `--recovery-bare20` compares local +20/+25/+30 candidates. Search failure is never treated as a proof of impossibility. These are investigation tools rather than replacements for the complete course, independent recovery, or browser gates.

## R6 runtime checkpoint

R6 uses distinct artifacts; the R5 browser data and reports are retained. The old `--runtime` source-equality command is expected to disagree after the geometry revision. Run the current checkpoint with:

```powershell
dotnet run --project Tools/RhythmTrapProof -c Release -- --courses Verification/rhythm-course-r6.json Verification/rhythm-course-r6-runtime-traps.json --runtime --browser Verification/rhythm-r6-routes.json
dotnet run --project Tools/RhythmTrapProof -c Release -- --recovery-r6
```

The course scope passes139,939 assertions /1,398 games. The targeted recovery scope maps78 historical actions to actual current R6 prefixes and passes19,217 checks, with first-delay samples0/5/10/15/20 ms and six dt. Read [RHYTHM-R6-ENGINE](../../Verification/RHYTHM-R6-ENGINE.md) for scope, hashes and the separate broad-recovery limitation. `--recovery-r6-before` is an isolated candidate investigation for the new before-full state, not a runtime modification.

Historical recovery searches load their explicit saved browser checkpoint (`rhythm-final-routes.json` for R5, `rhythm-r6-routes.json` for the new before-case study). They therefore remain reproducible after runtime track revisions. `--recovery-r6` deliberately uses current production tracks to test actual integration; historical command names must not be confused with the current integration scope.

## Final R7 integration

```powershell
dotnet run --project Tools/RhythmTrapProof -c Release -- --courses Verification/rhythm-course-r7.json Verification/rhythm-course-r7-runtime-traps.json --runtime --browser Verification/rhythm-r7-routes.json
dotnet run --project Tools/RhythmTrapProof -c Release -- --recovery-r7
```

The route proof passes139,939 assertions /1,398 actual games. The current-runtime recovery proof passes20,783 checks /84 actual perturbed prefixes, including all78 earlier targeted states and the six new Cascade before-exit cases. First-delay samples0/5/10/15/20 ms are replayed at six dt through the complete next obstacle union. No geometry override or state injection is used. See [RHYTHM-R7-ENGINE](../../Verification/RHYTHM-R7-ENGINE.md) for exact scope, controls, source hashes, exported browser data and the distinction from the independent broad audit.
