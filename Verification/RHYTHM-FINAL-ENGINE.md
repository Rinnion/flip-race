# RHYTHM final R5 — runtime geometry and negative-route proof

19 October 2026. This is the final author-engine checkpoint for the three R5 courses with two negative rings each. Earlier R2/R4 courses, before330 and offset192 remain historical candidates, including their recorded recovery failures. They are not presented as the final geometry. Catalogue activation, WebGL build/browser review and packaging are root-owned release checks.

The positive R5 geometry/routes are authored in `TracksRhythmFull.cs`. The separate `TracksRhythmNegatives.cs` adds two real optional circle zones through the `Draft` builder and exposes `NegativeRoute(id,flight)`. No vertical physics, presets, collision algorithm or effect accounting changed in this final step. The positive obstacles, rewards and safe/full input arrays match the composer's R5 JSON exactly.

## Final contract and results

Every course is7920 units and has **9 ordinary positive rewards, 2 negative rings and 1 separate finish reward**. The finish plane isX7876/Y32, height16, for all three courses. The full route ends at33.2 s; collecting exactly one negative on the final-gate branch produces33.5 s; safe remains36.0 s. Every reported route has zero collisions and fully completed ordinary pulse ledgers.

| Course | Middle negative X / Y | Final negative X / Y | Fast trap replacement | Main trap replacements |
| --- | --- | --- | --- | --- |
| rhythm-cascade |4035 /200|7705 /200|7669|7671,7695|
| rhythm-dense |3685 /200|7715 /200|7679|7681,7705|
| rhythm-waves |3450 /200|7725 /200|7689|7691,7715|

All negatives have radius8. Each sits80 units after the first band of an ordinary compact gate. Normal routes pass above the floor-side ring; a later departure deliberately collects it, then clears both gate parts. Only the last-gate inputs are replaced for the trap route; all earlier positive collection and the independent finish preparation remain intact. Both profiles retain their unchanged limits: Fast one airborne repeat, Main unlimited.

The final production builder and route API pass **143 590 assertions /1 416 complete games**:

- Both profiles, all three courses and dt1/30,1/60,1/144,0.317,2.5,100.
- Safe, full and trap outcomes36.0/33.2/33.5, exact reward budgets, zero hits and complete ledgers.
- Each safe input independently±60 ms and each full/trap input±25 ms against actual elapsed input time; later inputs remain distance-triggered.
- Fast inputs use actual support. Both players receive their own accepted input edge and finish tied under symmetric actions.
- Every runtime obstacle field, positive/negative zone field and safe/full/trap input array matches the frozen author source.

Evidence: [captured stdout](rhythm-course-r5-runtime-traps-proof.txt), [proof routes and times](rhythm-course-r5-runtime-traps.json). The windows are single-input perturbations, not a simultaneous jitter box. This suite does not substitute for independent mixed-player/recovery checks.

## Browser export

[rhythm-final-routes.json](rhythm-final-routes.json) is the complete runtime export for browser automation: six entries, one per course/profile, each with `id`, `name`, `profile`, `preset`10/11, immutable flight parameters, `safe`/`full`/`negative` X arrays, `inputs`/`safeInputs`/`negativeInputs` actual clock/state samples, complete obstacle fields and all12 zones. Gameplay does not consume these witness arrays as controls.

Each negative has latest contact `X+8+14`; a full slow then occupies66 world units of effect work. The final negative is therefore completed byX7793/7803/7813, before its module boundary7815/7825/7835 and the finish plane7876. The independent delivery audit owns the full all2 048-subset proof over all9 positive plus2 negative rewards, using the complete obstacle union for linked release, and mixed-player comparisons. That separate result must be checked for the final release; this local bound is not substituted for it.

## Reproduction

```powershell
dotnet run --project Tools/RhythmTrapProof/RhythmTrapProof.csproj -c Release -- --courses Verification/rhythm-course-r5.json Verification/rhythm-course-r5-runtime-traps.json --runtime --browser Verification/rhythm-final-routes.json
```

Use new output paths for a later revision. Shared Core regression, 40 228 JS snapshots /1 448 208 fields and restored old Flow51 048 checks are recorded separately in RHYTHM-CORE and RHYTHM-GATE-FAMILY. Before400 correction and module windows are documented in RHYTHM-MODULES. No old evidence was silently relabelled as final.

| Frozen source/artifact | SHA-256 |
| --- | --- |
| TracksRhythmFull.cs |`ACBBBC9ABF4F5646B9130A96FBF6186C325D8248FCFBB23F49F3CCB8C869E14C`|
| TracksRhythmNegatives.cs |`4623F6339193B3B098AB37EA81995EB3A1935D591FFFBC0F1E1D94E5A45E97F1`|
| rhythm-final-routes.json |`A784607B5CDD690551E0BDD6DC97E76668B167821E500141C14F01C3BA4C487A`|
| RaceSimulation.cs |`AA91B96919AE318607D8BFF180AE7A54879FA54C2C6072003F5B245A57E10C24`|
| ObstacleGeometry.cs |`BB1A5F4F79981CCDF3DABC4E9DB0511C3F9D9247B9E712ACC3FABB717653B92B`|
