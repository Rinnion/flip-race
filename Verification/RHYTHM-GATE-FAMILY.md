# RHYTHM-01 — compact and low gate families; restored Flow

19 October 2026. This supplements the historical [initial model proof](RHYTHM-CORE.md). It does not replace the original 192-offset results or the later recovery RED. Vertical physics, presets, repeat limits and the frozen Core gate helper were not changed during this refinement.

## Why the original shape was refined

The original width-16, offset-192, GapTop140/GapBottom100 gate passes isolated normal routes and joint input perturbations, but its first short stage has a real recovery failure. After a late Fast input, the next gate requires more rise than the remaining travel time allows after the penalty and 150 ms reaction. The independent Main late-airborne fixture also requires substantially more spacing. The actual witnesses remain in [recovery RED](rhythm-recovery-red.txt) and [historical pitch search](rhythm-recovery-pitch-witnesses.txt).

The author refinement compared five shapes using production Press/Advance, both mirrors and six frame steps. Fast normally uses one wall press. Main normally jumps and switches gravity 0.20 s later. Every normal route passes all combinations of −60/0/+60 ms on its one or two inputs at isolated speeds 110, 220 and 330. Constant non-base speeds deliberately use a long synthetic effect to isolate geometry; they are not claimed as collectible pulse durations.

| Offset / GapTop / GapBottom | Fast full window at 330 | Main full window at 330 | First found rescue pitch at 220, Fast / Main | At 330, Fast / Main |
| --- | --- | --- | --- | --- |
| 192 / 140 / 100 | 357.50 ms | 241.43 ms | 294 / 308 | 344 / 432 |
| 160 / 120 / 120 | 314.46 ms | 267.77 ms | 256 / 278 | 304 / 388 |
| 176 / 120 / 120 | 362.95 ms | 316.26 ms | 272 / 282 | 320 / 392 |
| 96 / 108 / 132 | 152.29 ms | 145.65 ms | 190 / 238 | 244 / 336 |
| **112 / 110 / 130** | **195.50 ms** | **182.21 ms** | **206 / 248** | **260 / 348** |

These are full timing intervals, not ± tolerances. Rescue figures are finite-search sufficient witnesses for a real late **wall** hit followed by the complete 0.5 s penalty and 0.15 s reaction, then clearance of the entire next gate. The catalogue searches pitches in 2-unit steps and rescue inputs at least 80 ms apart; it is not a global minimum and does not prove all possible error states recover. Main can use two airborne rescue switches, while the ordinary route uses one. The reported first-part bound assumes the normal airborne delay and is not a lower bound for every rescue sequence.

Compact112 was selected for the next stage. It has two 110-unit bands, open Y110…130, width 16 and signed order mirrored between floor and ceiling. The open height20 is smaller than the player diameter28, so there is no constant-height middle bypass. Independent audit of the troublesome **airborne** Main error found a real escape at pitch250, using an immediate input and another 0.4 s later; Fast found pitch230. Those are different initial states from the table, documented separately in [compact recovery](rhythm-compact112-recovery.txt). Stage placement and its sampled recovery matrix have their own proof.

For a first band anchored at X1000, selected compact112 launches are:

| Speed | Fast first X | Main first X | Main second input |
| --- | --- | --- | --- |
| 110 | 1034.575160678 | 1022.422438657 | +0.20 s |
| 220 | 1005.150321356 | 980.844877314 | +0.20 s |
| 330 | 975.725482034 | 939.267315971 | +0.20 s |

[Refinement v2 JSON](rhythm-offset-refinement-v2.json) and [stdout](rhythm-offset-refinement-v2.txt): **2 535 checks / 1 018 529 actual runs**. Earlier three-shape results remain in their original files.

## Low threshold first, tall part later

The common floor shape has a bottom threshold of height16 at X, followed by an upper band of height114 at X+80; both are width16. Its fields are GapTop114, GapBottom224, TopOffsetX80, BottomOffsetX0. The ceiling mirror is GapTop16, GapBottom126, TopOffsetX0, BottomOffsetX80. There is no diagonal collider.

The accessible Fast route starts on the **opposite** wall and uses one normal flip, arriving on the low-threshold wall. Main uses one ordinary jump from the low-threshold wall and returns to it. The additional expert Fast route starts on the low-threshold wall, passes over the small threshold and spends its single airborne repeat to return to that same wall. It is optional; it is not the only route through the gate.

Coordinates below use X1000 for the first physical part and work with either mirror:

| Speed | Fast accessible launch | Main jump launch | Fast expert launch / repeat delay | Verified expert local window |
| --- | --- | --- | --- | --- |
| 110 | 1012.458532408 | 949.886792453 | 970 / 0.20 s | each input ±40 ms |
| 220 | 976.917064816 | 891.773584906 | 950 / 0.15 s | each input ±40 ms |
| 330 | 941.375597224 | 833.660377358 | 936 / 0.13 s | each input ±40 ms |

Accessible Fast/Main inputs pass ±60 ms, both mirrors and all six steps. Expert perturbations move each input separately; this is not a claim about the whole joint ±40 ms box. The optional Main air-correction route uses a jump and switches at +0.20/+0.30 s, with separately verified ±30 ms local perturbations. It does not replace the simpler ordinary jump. Constant-speed isolation and the finite-search limits are explicit in the exported data.

[Low family JSON](rhythm-low-family.json) / [stdout](rhythm-low-family.txt): **14 280 assertions / 13 806 actual runs**.

Separate accessible-route fixtures collect real 0.6 s instant/negative effects. Boost ends at X986; slow ends at X1000 for Fast or X1003.363 for Main, at or within the first part. Normal gate clearance remains valid at ±60 ms on both mirrors and all frame steps. Shifted inputs may miss the optional test ring; the clearance assertion does not promise identical collection under every shift. These fixtures use real effect events, not held effects. [Pulse JSON](rhythm-low-pulses.json) / [stdout](rhythm-low-pulses.txt): **148 assertions / 428 games**.

## Existing Flow gates restored

Only the three formerly one-sided Flow gates changed. All have width16 and the common low family above:

| First X | First physical threshold | Later part |
| --- | --- | --- |
| 300 | bottom height16 at300 | upper height114 at380 |
| 1550 | upper height16 at1550 | bottom height114 at1630 |
| 6400 | upper height16 at6400 | bottom height114 at6480 |

Other obstacles, reward positions, IDs, the 9 ordinary + 1 finish reward budget, two negatives and finish plane are unchanged. Main safe/full/trap input arrays remain unchanged. Fast uses actual supported setup and passage inputs; its Flow safe array has9 inputs, full25 and trap25. The author harness still rejects an airborne Fast input on these ordinary routes, so changing the preset repeat limit to One did not weaken the grounding check.

[Author proof](rhythm-flow-restored-proof.txt): **51 048 checks PASS** over all three tracks and both profiles, six dt values (1/30, 1/60, 1/144, 0.317, 2.5 and100), safe ±60 ms and full ±25 ms. Outcomes remain safe36.0, full33.2 and trap33.5 seconds, with zero hits. All 2 048 ordinary-effect subsets per track finish before the last plane: conservative work bounds X7688/7728/7688, margins188/148/188. Release bounds use the complete physical obstacle union.

The new [browser route export](rhythm-restored-routes.json) contains both exact profiles, practice and three tracks, all safe/full/trap distance arrays, actual input clock times and complete obstacle/reward metadata. Historical modes and 4.5.x exports were not overwritten.

Independent checks on this TracksFull snapshot additionally found [15 gates with both physical halves](rhythm-restored-parts.txt), and [30 actual band contacts](rhythm-restored-contacts.txt), **1 981 assertions / 180 games**. Those contact fixtures are deliberately separate from authored-route feasibility and do not claim visual readability.

The separate restored-Flow audit passed **4 011 assertions / 192 games**, both profiles and six steps, safe inputs individually ±60 ms and full inputs ±25 ms, with exact finish ledgers. Its conservative all-positive-duration work bound completes at X7820.000001, leaving about56 units before the finish plane; the author bound above uses the shorter actual negative work. Its targeted recovery probe passed61 real first-hit states after the remaining penalty and150 ms reaction, reaching the next complete obstacle union. Evidence is in `rhythm-restored-flow-*.txt`; this finite recovery sample does not claim every possible input history.

## Reproduction and frozen files

From the Unity project root:

```powershell
dotnet run --project Tools/OffsetGateProof/OffsetGateProof.csproj -c Release -- --refine Verification/rhythm-offset-refinement-v2.json
dotnet run --project Tools/OffsetGateProof/OffsetGateProof.csproj -c Release -- --low-family Verification/rhythm-low-family.json
dotnet run --project Tools/OffsetGateProof/OffsetGateProof.csproj -c Release -- --low-pulses Verification/rhythm-low-pulses.json
dotnet run --project Tools/TwoModesProof/TwoModesProof.csproj -- --verify
dotnet run --project Tools/TwoModesProof/TwoModesProof.csproj -- --routes Verification/rhythm-restored-routes.json
```

These commands reproduce the current scope; choose a new output filename when retaining another historical run. No Unity process was launched by this proof task. The initial Core regression, old suites and 40 228 JS snapshots / 1 448 208 fields remain recorded in RHYTHM-CORE. Stage2, browser readability and recovery across its actual placement are separate release gates.

| Source | SHA-256 |
| --- | --- |
| TracksFull.cs | `901255021DD6069E43734E80D16B88E5DC82824CB973239A451D8999317FA576` |
| RaceSimulation.cs | `AA91B96919AE318607D8BFF180AE7A54879FA54C2C6072003F5B245A57E10C24` |
| ObstacleGeometry.cs | `BB1A5F4F79981CCDF3DABC4E9DB0511C3F9D9247B9E712ACC3FABB717653B92B` |
