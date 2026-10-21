# Mixed course: local geometry, negatives and targeted recovery

21 October 2026. Final `TracksMixed.cs` SHA256 `01C67639E567F2325D668678786AE5EAFDB5352C8204D8CC876EF288645210E0`; this differs from the tested `4658023AE889FFAAD2AE8CEB85A842A5214B3FCA8A7950D9731C6F43D8905BBE` only by removal of an obsolete draft comment. Geometry, fields and routes are unchanged. `RaceSimulation.cs` remains `AA91B96919AE318607D8BFF180AE7A54879FA54C2C6072003F5B245A57E10C24`. The course author owns production geometry/routes. This task changed only its own proof tools and reports after the separate MIX-01 stage freeze.

## Reproduction and results

Run from the Unity project root:

| Command suffix after `dotnet run --project Tools/MixedStageProof -c Release --` | Result | Evidence |
|---|---:|---|
| `--straight` | 3,270 checks / 176 games | `mixed-straight-proof.json/.txt`, [MIX-STRAIGHT.md](MIX-STRAIGHT.md) |
| `--negative-proof --source Verification/mixed-course-windows-r2.json` | 34,650 checks / 579 games | `mixed-negative-proposal.json`, `mixed-negative-proposal-r2.txt` |
| `--course-recovery` | 1,707 checks / 12 actual-prefix baseline games / 132 continuation replays | `mixed-course-recovery-targeted.json/.txt` |
| `--course-native` | 203 checks / 3 full games | `mixed-course-native-pad.json/.txt` |

The positive r1 search and r2 proposal outputs are separate. The definitive runtime ten-route export is `mixed-course-r2-routes.json`, SHA256 `B09B599216E014CD82FEE82296EF41AF10892BF7CDFD914BF493B0A6A0A188EA`. The positive-only r2 snapshot used to propose negatives is `mixed-course-windows-r2.json`, SHA256 `3D87356CA49C837FCD74CD23806B4000836E025EFADB0B4D8781D3875F4402A3`.

## Straight and actual speed transitions

The added straight form has both real bands at the same X: width 8, upper height 48 and lower height12. Main naturally jumps and lands on the original wall; Fast uses one airborne return and also reaches the original wall with charge restored. This provides a different action from a monotone wall swap, while remaining a broad asymmetric window. It is not described as a narrow central opening.

Both mirrors and six frame sizes pass. At 220/330, individual input windows of ±60 ms pass for both profiles. Fast held 110 uses a reachable service alternative certified only at ±25 ms. Separate actual instant/negative prefix rings expire inside the gate, changing 330/110 back to220 while crossing. These real-pulse cases preserve input acceptance, continuous velocity, wall recovery and actual finish accounting without injecting dynamic state. See the separate local report for exact coordinates and bounds.

## Negative choices and route consequences

Two negative circles at `(3820,180)` and `(6500,180)`, radius 8, are avoided by every original r2 safe input shifted individually ±60 ms and every full input shifted individually ±25 ms. They do not change positive geometry or reward positions.

Intentional first-negative routes add Main input 3762.5, or Fast inputs 3702.5/3739.9. Intentional second-negative routes replace Main 6400/6444 with 6442.5/6475.5, or Fast 6400 with 6445. The new or changed trap inputs pass individual ±25 ms. Each trap retains all nine ordinary positives and the final target: 33.5 seconds with one slow; the combined route finishes 33.8 with both slows. All six frame sizes pass, both players match, and every earned ordinary effect completes naturally. These counts are independently replayed against the positive JSON copy plus two proposed zones; the author additionally verifies every input on the final runtime trap arrays.

The first negative is near the transition into the central-offset episode; the second precedes the mirrored small-stone episode. Avoidance remains a choice rather than an automatic penalty for the safe path. No old Fast round trip removed in r2 was restored to the safe/full route by this proposal.

## Bounded recovery

The author's finite scan captured 244 actual first-hit states and found stay/one/two-input continuations for 239. The remaining five were the same Main low-floor state reached through different real safe/full prefixes. They were not declared physically impossible.

Each of those five was reconstructed from the final r2 exported route and its shifted input, using real `Press`/`Advance`. At the hit, X3616/Y83.4296/VY−174.6591. After the residual .5-second penalty and an additional 150 ms reaction, X3649/Y49.7776, still airborne. The valid continuation is:

1. Wait for genuine ceiling support, around X3674.3.
2. Jump from that wall; change gravity after 200 ms.
3. Wait for genuine floor support.
4. At X4030.844877314131, jump; change gravity after 200 ms and clear the next complete offset union through X4193.

The first wall action also accepts an extra 0/10/20 ms delay. All five real prefixes, all six frame sizes and all three delays pass: 90 continuation replays. No coordinates, velocity, charge or effects are assigned. The same physical support events decide when to continue; this is not an absolute timer retuned for each failing prefix.

Seven additional actual first-hit prefixes independently replay the author's provided witnesses, one for each remaining requested type/profile: straight→low, low→offset, large-pad→after, final-offset→low. Each passes all six frame sizes, reaching `ObstacleGeometry.Right(next) + 15` without a second hit in either player. Together with the Main low class, all four boundaries are covered for both profiles: 42 additional replays, 132 total. This limited replay is not a second exhaustive scan or a guarantee that every conceivable error can be rescued.

## Known native timing risk

Individual full-route ±25 ms does not imply a joint box. The Main large-pad trio has a specific alternative suitable for frame-driven native input: after the actual launch at 5026, use the sixth and tenth subsequent frame. At 32 Hz the resulting inputs are 5026/5067.25/5094.75; at 30 Hz they are 5026/5070/5099.3333333. Both complete the actual full route with zero hits, nine ordinary positives, one finish reward and 33.2 seconds. This is a narrow reproducible witness, not a simulation of an entire browser input/render pipeline.

## Limits and division of evidence

Main's natural small jump, the Fast small/straight One returns, the required large-stone side change, optional exact narrow/wide stone supports, low-window passages, and the second chain action under an earned 330-speed effect are recorded by the author’s semantic traces. The final course contains five gates with separate straight, low and offset forms; it does not repeat compact gates to connect every scene. Bounded shortening removed unnecessary Fast round trips before this proof snapshot.

MIX-01 browser results and the Fast wrong-wall-touch/joint±60 limitation remain in [MIX-01-ENGINE.md](MIX-01-ENGINE.md). Held-speed fixtures are explicitly separate from actual earned effects; local straight 110 ±25 is not silently promoted to a mandatory ±60 guarantee. Full-course effect-tail bounds belong to the parent's separate `MixedTailCheck`, and Unity/browser/package validation belongs to the parent release report. No additional Core change, production mutation, Unity launch, commit or publication was performed by this proof task.
