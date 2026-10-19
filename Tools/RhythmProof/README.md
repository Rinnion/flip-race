# RHYTHM-01 stage 2 proof

Run from the Unity project root with .NET 10:

```powershell
dotnet run --project Tools/RhythmProof/RhythmProof.csproj --configuration Release -- --verify
```

The tool uses production Core and the shared `TrackProofControl/Harness.cs`; it does not change gameplay, Core, input handling or authored geometry. The default command verifies/exports the four complete routes. `--recovery` runs the bounded actual-error recovery catalogue. `--straight-slow` isolates the straight gate at held speed 110. `--verify` runs all three.

Current stage: `TracksRhythm.Practice()`, 4600 world units, 20.909090909 seconds at its natural speed 220. Eight compact offset gates are followed by a straight gate and one low two-part gate. Both physical parts are nonzero for every gate. `PracticeRoute(flight)` is the ordinary route; `PracticeQuickReturnRoute(flight)` provides the separate optional low-gate path on the same geometry. These arrays are proof/browser fixtures, never autonomous gameplay controls.

| Profile | Normal inputs | Optional inputs | Normal tolerance | Optional low tolerance |
|---|---:|---:|---:|---:|
| Main, preset 10 | 18 | 20 | each input ±60 ms | each of its three inputs ±30 ms |
| Fast, preset 11 | 10 | 12 | each input ±60 ms | departure/repeat each ±40 ms |

Optional Fast includes a real ceiling-to-floor setup, then a departure and its single permitted air return. Main has a natural jump normal route and a separate two-air-correction route. Both optional flights finish on their original floor. Inputs, actual support state, clocks and per-input tested tolerances are exported to `Verification/rhythm-stage2-routes.json`.

All four complete routes run at 30/60/144 Hz and maximum steps .317/2.5/100 s. Tests assert real wall recharges, accepted airborne repeats, clean finishes and actual same-wall landings. Every action of the ordinary route is separately removed and causes a collision; optional routes make no global minimum-tap claim. The six-gate middle group contains six useful Fast presses, with actual intervals 1.636–1.682 s.

Recovery captures the first real collision after one input is shifted by −640/−400/−160/+160/+400/+640 ms. After the remaining penalty and 150 ms reaction, it searches accepted controls and requires physical arrival beyond the next gate's complete union bound +15, without another hit. For some Main mistakes the continuation includes a settling jump/repeat, actual wall landing, and the next gate's normal jump/repeat. The 98 captured states pass. This is a finite sample through the next obstacle, not an arbitrary-history guarantee; a final-obstacle sample reaches the tape.

At held 110 the straight gate cannot use the ordinary Fast single flip. A separate real departure + one air return succeeds on both walls at all six step sizes, and a second air repeat remains rejected. The fixture injects a long slow effect to isolate geometry; it is explicitly not a reachable reward history in this reward-free stage. Compact-gate 110/220/330 joint windows and low-family speed/effect transitions are independently recorded by the engine author in `rhythm-offset-refinement-v2.json`, `rhythm-low-family.json` and `rhythm-low-pulses.json`.

Results: complete routes **11,943 checks**; all author checks including 98 recovery states and 24 slow-straight games **23,095 checks**. See `Verification/RHYTHM-STAGE2.md` for the source checkpoint and independent evidence. Browser and human usability are separate checks.

Stage 1's offset-192 source and proof are retained as `Verification/rhythm-stage1-192-TracksRhythm.cs.txt` and `rhythm-stage1-192-proof.cs.txt`. The original `rhythm-stage-routes.json`, UI diagram/layout and browser results were not overwritten and do not describe stage 2.
