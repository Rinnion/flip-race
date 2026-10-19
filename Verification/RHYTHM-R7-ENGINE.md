# Final R7 author and targeted recovery proof

R7 is the final author snapshot supplied for independent audit and WebGL/browser validation. It changes only the selected Cascade spacing after the before module, with compensating rest lengths. Dense and Waves retain their R6 geometry. Core physics, presets, collision rules and the catalog `All` are unchanged by this work.

## Complete routes

[Runtime proof output](rhythm-course-r7-runtime-traps-proof.txt): **139,939 assertions /1,398 actual games**. All three courses, both Chosen and FastFlip, and six maximum time steps (30/60/144 Hz,0.317,2.5,100 seconds) pass:

| Route | Actual finish | Ordinary positive | Negative collected | Finish reward | Collisions |
|---|---:|---:|---:|---:|---:|
| Safe |36.0 s|0|0|0|0|
| Full |33.2 s|9|0|1|0|
| Trap |33.5 s|9|1|1|0|

Each safe input is independently perturbed by ±60 ms of actual elapsed time. Every full and trap input is independently perturbed by ±25 ms. Subsequent inputs remain distance-triggered. Production geometry, zones and all input arrays match the final author manifest. Every authored game completes its earned ordinary pulse ledger before actual finish. The broader universal effect-tail audit remains independent of these route witnesses.

The exact six-row browser export is [rhythm-r7-routes.json](rhythm-r7-routes.json). It includes profile/preset, safe/full/negative X arrays, actual input clocks and state samples, obstacles and zones. Its SHA-256 is `E385F64CCDB816385B210A0D9FAA3BD283FFD74262E1A017A38D9217EEBADDFB`.

## Actual runtime recovery

[Targeted output](rhythm-r7-recovery-targeted.txt): **20,783 checks /84 actual perturbed prefixes**.

- [Mapped78](rhythm-r7-recovery-mapped78.json): the prior72 bare-air and6 before-air cases, including33 mirrored cases; **19,217 checks**.
- [New six](rhythm-r7-recovery-new-six.json): Cascade full/trap input10 delayed by0.4/0.64/0.8 seconds; **1,566 checks**.

Every prefix starts from a real race and uses production `Press`/`Advance`. The first hit's logical ID is checked. Recovery consumes the remaining penalty and a further150 ms reaction interval. First-input delays0/5/10/15/20 ms are checked at all six dt; the player must clear the entire next obstacle's union right edge +14+1 without another collision. Both players are checked. No player state is injected and no temporary geometry override is used in this final integration scope.

The bare-air continuation uses actual wall contact, jump and a repeat50 ms later. The six new Cascade cases use two actual wall transitions, with jump-repeat intervals90 and100 ms. Other mapped before cases retain their explicit four-input catalogue. These are finite tested samples, not an assertion about every possible mistake or human comfort. The independent broad audit remains the source for the wider error sample.

## Final negative placement

| Course | Middle X/Y | Final X/Y | Latest final slow work end |
|---|---|---|---:|
| Cascade |3977 /200|7705 /200|7793|
| Dense |3831 /200|7709 /40|7797|
| Waves |3385 /200|7725 /200|7813|

Each radius is8. All courses keep exactly two negative rings, nine ordinary positive rewards and one separate finish-plane reward at X7876. Dense's last gate and negative ring are mirrored. The finish target remains unchanged by this negative update.

## Commands and frozen source

```powershell
dotnet run --project Tools/RhythmTrapProof -c Release -- --courses Verification/rhythm-course-r7.json Verification/rhythm-course-r7-runtime-traps.json --runtime --browser Verification/rhythm-r7-routes.json
dotnet run --project Tools/RhythmTrapProof -c Release -- --recovery-r7
```

- RaceSimulation: `AA91B96919AE318607D8BFF180AE7A54879FA54C2C6072003F5B245A57E10C24`
- TracksRhythmFull: `8B62C3256FBCD3F53A8BFC90D552640C73AC43A0FC3A04251B785F5DAD2088C4`
- TracksRhythmNegatives: `75AC8DD76890190D5CD1BD2186D62A31015FFBA31B196A3AFC6A932A347BB3E2`

Historical R5 and R6 browser exports are preserved byte-for-byte: `A784607B5CDD690551E0BDD6DC97E76668B167821E500141C14F01C3BA4C487A` and `A09996A3540E10981B15C09E67DB69FCE14655D5512F1993BD1E2F24234041A8`. The earlier R7candidate1 included an additional Waves change; its artifacts are retained as a candidate and are not this final snapshot.
