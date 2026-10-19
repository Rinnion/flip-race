# RHYTHM — negative-ring prototypes and R2 checkpoint

19 October 2026. This is author evidence for the local optional branch and the positive-course R2 snapshot. R2 is not the final release: the composer is revising Dense spacing/shortcut behavior, and a separate before-module recovery audit remains pending. No vertical physics or reward accounting was changed.

## Local candidate

For a compact gate with first physical X, width16, offsets0/112, GapTop110/GapBottom130, place the negative ring at firstX+80, Y200 on a floor approach, mirrored to Y40 for a ceiling approach, radius8. The initial firstX+65 proposal was rejected: Fast normal input+60 ms collected it, despite zero hits. This would have broken the promised safe timing window.

The firstX+80 placement passes both mirrors and six dt values. Ordinary inputs avoid it at individual±60 ms. Fast's optional branch presses at firstX+44; each input window is±75 ms. Main's selected branch presses at firstX+46 and+70; the actual interval is159.172 ms because the first part of the flight collects the slow. Both Main inputs individually pass±50 ms. The suggested +35/+68 pair passes nominally but has less than5 ms window and is not selected.

The ordinary gate trajectory and the trap branch both finish on the opposite real wall with the whole slow pulse completed by firstX+190. Latest possible contact is firstX+102 and a slow serves66 world units, so its latest end is firstX+168, leaving22 units to that module boundary.

| Scope | Result | Evidence |
| --- | --- | --- |
| Local geometry, ordinary avoidance and finite trap search |33 079 assertions /5 467 games PASS|[stdout](rhythm-trap-local-v2.txt), [JSON](rhythm-trap-local.json)|
| Main branch with real interval≥150 ms |11 685 assertions /1 861 games PASS|[stdout](rhythm-trap-main-alternative-v2.txt)|

Search windows are independent single-input perturbations; they are not joint jitter boxes. Actual wall setup, negative pickup, recovery-free motion and pulse completion use production Press/Advance, not injected dynamics.

## R2 authored courses

Two negative rings are attached to ordinary bare-gate passages in each course, one in the middle and one at the end. Positive geometry, existing reward locations and safe/full distance inputs remain unchanged. The negative route replaces only the ordinary last-gate action(s); the independent finish preparation is retained.

| Course | Middle X / Y | Final X / Y | Fast replacement | Main replacements |
| --- | --- | --- | --- | --- |
| Cascade |4035 /200|7725 /200|7689|7691,7715|
| Dense |3515 /200|7735 /40|7699|7701,7725|
| Waves |3400 /200|7675 /200|7639|7641,7665|

All rings have radius8. `TracksRhythm.Draft` adds them through a separate partial file, and `NegativeRoute(id,flight)` preserves the rest of `FullRoute`. The model's two negative zones are real optional circle events; there are no scripted speed changes.

The frozen R2 JSON with proposed negatives passes147 330 assertions /1 434 complete games. The actual runtime builder/route API then passes **147 360 assertions /1 434 games**, additionally matching all obstacle fields, positive zone fields and safe/full/trap input arrays against the frozen author export. Both profiles, all three courses and six dt values finish safe36.0/full33.2/trap33.5 with zero hits; every safe input individually±60 ms and every full/trap input±25 ms also passes. Trap has9 ordinary positives, one finish reward and exactly one slow, with all pulse ledgers completed.

Runtime checkpoint evidence: [stdout](rhythm-course-r2-runtime-traps-proof.txt), [routes/actual times](rhythm-course-r2-runtime-traps.json). The non-runtime proposal is retained separately in `rhythm-course-r2-traps.json`. Final-source composition, all-subset tail and final browser verification remain separate gates.

```powershell
dotnet run --project Tools/RhythmTrapProof/RhythmTrapProof.csproj -c Release
dotnet run --project Tools/RhythmTrapProof/RhythmTrapProof.csproj -c Release -- --alternative
dotnet run --project Tools/RhythmTrapProof/RhythmTrapProof.csproj -c Release -- --courses Verification/rhythm-course-r2.json Verification/rhythm-course-r2-runtime-traps.json --runtime
```

The last command intentionally checks the exact runtime against R2 and may fail after a later course revision; use the corresponding new export and a new output filename rather than rewriting historical evidence.
