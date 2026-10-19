# FastFlip one-repeat correction — 4.5.1

Date: 19 October 2026.

The runtime change is exactly two substitutions: `FlightPresets.FastFlip.AirRepeats` is `One`, and `TracksFull.Fast` recognizes that same preset. Gravity 2700, departure impulse 35, all inactive values, geometry and all input-distance arrays are unchanged. `RaceSimulation.cs` is unchanged. Manual `None` still rejects airborne input; `Chosen` still permits unlimited repeats.

## Targeted behavior

`EngineModesTests` now exercises real presses from both walls and mirrored 64 × 48 landing platforms, with steps 1/30, 1/60, 1/144, 0.317, 2.5 and 100 seconds. The first airborne repeat preserves position and velocity and reverses acceleration. A second airborne repeat is rejected. The other player retains their own charge and motion. A late reversal's inertial contact with the opposite wall does not recharge; a real wall or legal pad landing does. The refreshed charge again permits exactly one repeat. Manual `None` and the unlimited primary preset are also checked.

The current audit's authored-route grounding checks use physical profile identity rather than `AirRepeats == None`. This retains all prior checks that Fast route inputs originate from actual support. Its natural narrow-platform walkoff remains a no-rescue witness; a deep snapshot independently tests the newly available optional repeat without changing that original fall.

## Results

| Command/scope | Result | Saved stdout |
| --- | --- | --- |
| ParityRunner `--modes` | PASS 4 scenarios / 1 190 assertions, including the previous gate/linked-contact regressions | [flip451-core.txt](flip451-core.txt) |
| TwoModesProof `--verify` | PASS 48 596 checks | [flip451-author-proof.txt](flip451-author-proof.txt) |
| TwoModesAudit `analytics` | PASS 2 367 assertions / 72 games | [flip451-analytics.txt](flip451-analytics.txt) |
| TwoModesAudit `stage` | PASS 2 490 assertions / 90 games | [flip451-stage.txt](flip451-stage.txt) |
| TwoModesAudit `full` | PASS 29 693 assertions / 296 games | [flip451-full.txt](flip451-full.txt) |

All three full tracks retain safe **36.0 s**, full **33.2 s**, and trap **33.5 s**, for both profiles at all six steps. The author verification also reruns finite safe ±60 ms / full ±25 ms input perturbations and all 2 048 ordinary-effect subsets. Conservative weighted completion remains X7688 / X7728 / X7688, before the finish plane X7876. The audit full scope retains 146 individual safe ±60 ms shifts and both assignments of the final-only paired duel. Practice remains safe 15.0 s / full 13.5 s.

The previous 4.5.0 reports, JSON exports and standalone audit snapshot are preserved. The 9 465-simulation window search and recovery research were not rerun for this correction. These commands are .NET checks; the separate root build/browser report covers WebGL delivery.

## Reproduction

Run from the Unity project root with .NET 10:

```powershell
dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- --modes
dotnet run --project Tools/TwoModesProof/TwoModesProof.csproj -- --verify
dotnet build Tools/TwoModesAudit/TwoModesAudit.csproj
dotnet Tools/TwoModesAudit/bin/Debug/net10.0/TwoModesAudit.dll analytics
dotnet Tools/TwoModesAudit/bin/Debug/net10.0/TwoModesAudit.dll stage
dotnet Tools/TwoModesAudit/bin/Debug/net10.0/TwoModesAudit.dll full
```

## Frozen SHA-256

| Source | SHA-256 |
| --- | --- |
| `Assets/Scripts/Core/FlightSettings.cs` | `8499B6DC0D17A86A9CE02CE3F56FA4BD78D4F66CA1CB61E85B052C6BE5C9FB5E` |
| `Assets/Scripts/Core/TracksFull.cs` | `8B4524372B5F27DD94CA7D072857493CB6B627BA812426E1B4194DC4382A09C1` |
| `Assets/Scripts/Core/RaceSimulation.cs` (unchanged) | `CFA0A419C9CE9FC7C0F2A7F075CACC8E7CD3C9655D12412037AA2E5D4BC7838B` |
| `Assets/Editor/EngineModesTests.cs` | `6960DC56767F68A44DEE41C7C6D49BAAEA0507909E32EA69C1E1F136EE6E59CE` |
