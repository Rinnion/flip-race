# Rhythm course composer

This offline tool composes the three authored courses from independently verified module geometry. It uses the production `RaceSimulation`, immutable profile presets, and the actual input/effect clocks. The game does not read these schedules as controls.

The current candidate is **R7**. Earlier revision exports are historical snapshots; running the current source with an old `--revision` label does not reconstruct those revisions. `Program-r5.cs.txt`, `Program-r6.cs.txt` and `Program-r7-candidate1.cs.txt` preserve the earlier placement source.

Run from the Unity project directory:

```powershell
dotnet run --project Tools/RhythmCourseCompose/RhythmCourseCompose.csproj --configuration Release -- --revision=r7 --windows
dotnet run --project Tools/RhythmCourseCompose/RhythmCourseCompose.csproj --configuration Release -- --revision=r7 --reduce
dotnet run --project Tools/RhythmCourseCompose/RhythmCourseCompose.csproj --configuration Release -- --runtime-only
```

The first command writes `Verification/rhythm-course-r7.json`, the exact module input snapshot, and a C# source candidate under this tool directory. It never writes Assets or activates the catalogue. The second command writes the finite input-deletion search. The third compares the positive geometry, names, cues, groups and both profiles' route arrays against the emitted runtime source; optional negative zones are checked separately.

R7 uses `rhythm-modules-r6.json` (before exit X420/end610, chain second gate/ring +30/end720), explicitly takes the trimmed `large` from `rhythm-modules-large580.json`, and delays the Fast safe after-module return from X217 to X340. Eight selected Cascade/Waves boundaries gain 30 units for recovery. Cascade additionally gains 20 units after its before-module. Dense removes one bare gate and distributes the available space between modules; it keeps the gate separating large and pad. The route-only after change is validated by the complete-course ±60 ms tests. Each instance prefixes obstacle IDs, reward IDs and linked obstacle IDs. Mirroring swaps floor/ceiling, Y, gate gaps and the two physical X offsets.

Every module boundary requires an actual wall support on the expected side, zero vertical velocity, no flight, recharged air repeat, speed 220, no active/queued effect and no pending linked reward. The whole-course tests cover both profiles, both players, six integration steps and independent ±60 ms safe / ±25 ms full inputs. These are independent input perturbations, not simultaneous adversarial perturbations of the whole route.

The deletion search tries each individual omission and greedy contiguous blocks of one to four inputs. It is a finite check for simple avoidable actions, not a proof of global minimum inputs. Recovery is verified in separate independent module/course audits; nominal boundary checks do not claim every collision history can recover.

Runtime API: `TracksRhythm.Draft(id)`, `SafeRoute(id, flight)`, `FullRoute(id, flight)` and `Groups(id)`. The negative partial owns `NegativeRoute(id, flight)` and the two optional traps. `TracksRhythm.All` activation is a separate release gate.

See `Verification/RHYTHM-COURSE-R7.md` for the current measured rhythm and evidence. The R5/R6 reports and raw results remain historical checkpoints.
