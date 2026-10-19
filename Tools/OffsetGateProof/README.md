# Offset-gate CPU proof

Run from the Unity project root with .NET 10:

```powershell
dotnet run --project Tools/OffsetGateProof/OffsetGateProof.csproj -c Release -- Verification/rhythm-offset-proof.json
dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- --offset-gates
```

The common candidate uses upper Y0–140 and lower Y100–240, width 16 each, separated by 192 in X. The first part starts at X1000. Floor entry puts the upper part first; the ceiling mirror puts the lower part first. There is no shared straight center-height corridor. The default proof uses one production `Obstacle`; optional `--paired` uses the original equivalent two-object fixture with a common ID for comparison.

FastFlip uses one accepted ground press. Chosen uses a jump and one airborne gravity change after 0.20 s. Both begin on a real wall; the ceiling setup uses actual presses. Every candidate is exercised at 1/30, 1/60, 1/144, 0.317, 2.5 and 100 s steps, on both surfaces. First/second inputs are perturbed independently by −60/0/+60 ms, including the nine combined Main cases. The search reports wider individual finite windows separately; a reported 150 ms means the search cap was reached, not a maximal boundary.

Constant110/330 fixtures deliberately hold a synthetic speed effect longer than 0.6 s to isolate geometry. They do not claim those durations are collectible in the game. Separate fixtures collect real instant/negative rings: ordinary pulses end inside the gap, or begin there and continue toward/through the second part. The nominal contact/completion X values are exported. Perturbations must clear the gate; a shifted path may miss an optional ring, so those windows do not promise collection at every shifted input. These are local mandatory-obstacle timing checks, not full-track or audience-playtest evidence.

Production targeted tests additionally check signed offsets, absent-band bounds, exact inclusive corner contacts, logical single penalty with a live neighboring obstacle, and linked clearance after the farthest physical part. Full details and recorded regression outputs are in [RHYTHM-CORE](../../Verification/RHYTHM-CORE.md). Existing reports are not overwritten.

## Refinement and low gate family

The default 192-offset case above is retained as historical evidence. Its short stage later exposed a recovery failure, so a normal-route PASS must not be read as a universal recovery claim. The current compact candidate is offset112, GapTop110/GapBottom130, width16. The low family places a threshold16 first and a tall band114 at +80, width16, with a mirrored ceiling form. Normal routes and the optional Fast same-wall quick return are separate witnesses.

```powershell
dotnet run --project Tools/OffsetGateProof/OffsetGateProof.csproj -c Release -- --refine Verification/rhythm-offset-refinement-v2.json
dotnet run --project Tools/OffsetGateProof/OffsetGateProof.csproj -c Release -- --low-family Verification/rhythm-low-family.json
dotnet run --project Tools/OffsetGateProof/OffsetGateProof.csproj -c Release -- --low-pulses Verification/rhythm-low-pulses.json
```

Use a new output path to preserve an earlier run. `--refine` tests five shapes, joint normal ±60 ms inputs and a finite rescue catalogue after real wall hits. It does not cover every possible airborne collision state. `--low-family` isolates speeds110/220/330, checks both mirrors and six dt values, and reports individual expert input windows. `--low-pulses` uses real 0.6 s effects for accessible routes instead of held-speed isolation. Scope, limits, selected coordinates, old recovery RED and restored Flow results are documented in [RHYTHM-GATE-FAMILY](../../Verification/RHYTHM-GATE-FAMILY.md).
