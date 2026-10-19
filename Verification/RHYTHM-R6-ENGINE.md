# R6 author proof and runtime checkpoint

This is the R6 CPU checkpoint. Release readiness also depends on the independent broad recovery audit and browser verification. The broad audit has identified a new before-exit full-route state; its tool-only spacing candidates are recorded separately and have **not** been applied to runtime. This document does not declare the release complete.

## Module and course proof

- Updated before module: exit gate X420, end610, conservative work end457.
- Updated chain: second gate X450, second ring X484, end720, conservative work end704.
- Module export: **71,493 assertions /5,731 actual runs**, both profiles, mirrors and six dt; individual safe ±60 ms/full ±25 ms, real wall support and empty effects.
- Chain reward subsets: **1,872 assertions /96 runs**, neither/first/second/both rewards, both profiles and mirrors, six dt.
- Final R6 runtime safe/full/trap author proof: **139,939 assertions /1,398 actual games**. All three courses on both profiles finish at36.0/33.2/33.5 seconds respectively, with zero collisions. Every safe input is independently shifted ±60 ms; every full and trap input ±25 ms. Six dt cover30/60/144 Hz,0.317,2.5 and100 seconds.
- Budget remains nine ordinary positive rewards, two avoidable negative rings and one separate finish reward. The trap route collects exactly one negative; safe/full collect none. The earned ordinary pulse ledger is completely served by actual finish.

The runtime obstacle geometry, positive/negative fields and safe/full/trap arrays are compared against the R6 author export. The six browser rows include exact X inputs, actual input clocks and state samples, geometry, zones, profile and preset IDs.

```powershell
dotnet run --project Tools/RhythmTrapProof -c Release -- --courses Verification/rhythm-course-r6.json Verification/rhythm-course-r6-runtime-traps.json --runtime --browser Verification/rhythm-r6-routes.json
dotnet run --project Tools/RhythmTrapProof -c Release -- --recovery-r6
```

[Course output](rhythm-course-r6-runtime-traps-proof.txt), [browser export](rhythm-r6-routes.json), [module output](rhythm-modules-r6-proof.txt), [chain subsets](rhythm-chain-r6-subsets.txt).

## Negative rings

| Course | Middle X/Y | Final X/Y | Latest final negative work end |
|---|---|---|---:|
| Cascade |3971 /200|7705 /200|7793|
| Dense |3831 /200|7709 /40|7797|
| Waves |3385 /200|7725 /200|7813|

All radii remain8. Dense's final gate is mirrored after the course recomposition; its negative ring is correspondingly at Y40. The independent all-subset tail calculation remains a separate audit, rather than being inferred from these authored routes alone.

## Actual R6 targeted recovery

[Targeted output](rhythm-r6-recovery-targeted.txt) and [exact witnesses](rhythm-r6-recovery-targeted.json): **19,217 checks /78 actual modified prefixes**, covering the prior72 bare-air and6 before-air cases, including33 mirrored cases. Old action/module IDs are mapped to current R6 routes and current baseline clock times; every first collision retains the expected logical obstacle ID.

Each state waits the remaining0.5-second penalty and a further150 ms reaction period. Five additional first-input delays (0/5/10/15/20 ms) are replayed at six dt. The target is the entire next physical obstacle's union right edge +14+1. The bare-air continuation uses the actual opposite-wall landing, jump and a repeat50 ms later; the before-air cases use their four-input catalogue. There is no player-state injection and no temporary geometry override in this R6 scope.

This closes those78 mapped states, not every possible new state after recomposition. The new independent before-full case and candidate +10/+15/+20 suffix tests are preserved in [candidate output](rhythm-r6-before-newclass-candidates.txt) and [candidate witnesses](rhythm-r6-before-newclass-candidates.json). No candidate from that file is silently included in this checkpoint.

## Frozen hashes

- RaceSimulation: `AA91B96919AE318607D8BFF180AE7A54879FA54C2C6072003F5B245A57E10C24`
- TracksRhythmFull: `D347836E5909354CF064E9623DF0B250C8573E89713CDE3CD695248381EF9699`
- TracksRhythmNegatives: `52B4BE55A6BF1FD1FDAC08EBE2285D9BE8FCEED1545A814ED67973CCB8732862`
- R6 browser export: `A09996A3540E10981B15C09E67DB69FCE14655D5512F1993BD1E2F24234041A8`

The historical R5 `rhythm-final-routes.json` remains unchanged at `A784607B5CDD690551E0BDD6DC97E76668B167821E500141C14F01C3BA4C487A`. The catalog `All`, Core physics and collision rules are not changed by this work.
