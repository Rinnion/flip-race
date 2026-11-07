# Dense chains: bounded probe

Tool-only replay of production `RaceSimulation.Press/Advance`. No production files, tracks in menus, build, or port are changed. Only two obstacle geometries exist: four sequential gates, and six aligned shallow gate pairs.

The original exploration stopped under the 20-minute limit on 31 October 2026. Its absolute deadline remains enabled for exploration. `--replay` allows later replay of the same fixed cases with a separate 30-second execution cap. No package installation is required.

From `flip-race-unity`, use an unused tag to preserve prior evidence:

```powershell
./Tools/DenseChainsProbe/Replay.ps1 -Tag independent
```

This runs the four commands below, sequentially. Each output includes all coordinates and accepted/rejected inputs. A repeated output filename is refused rather than overwritten.

```powershell
dotnet run --project Tools/DenseChainsProbe/DenseChainsProbe.csproj -c Release -- --replay --tag=-independent
dotnet run --project Tools/DenseChainsProbe/DenseChainsProbe.csproj -c Release -- --parallel --replay --tag=-independent
dotnet run --project Tools/DenseChainsProbe/DenseChainsProbe.csproj -c Release -- --recovery --replay --tag=-independent
dotnet run --project Tools/DenseChainsProbe/DenseChainsProbe.csproj -c Release -- --entrances --replay --tag=-independent
```

Expected results: sequential geometry clean for both profiles, six accepted inputs each, every omission causes an actual collision; actual instant boost earned/completed once; Chosen center bypass clean. The entrance/switch tapes are deliberately finite failed attempts, not an adaptive solver or evidence that all switches are impossible. Recovery tapes demonstrate one FastFlip hit followed by a clean remainder, and a Chosen cascade under an unadapted continuation.

Interpret timing as independently perturbed inputs on a finite grid; other inputs remain distance-triggered. The physical integration step is at most 5 ms. Landings are sampled within a step, not exact analytic contact timestamps. No all-FPS, joint timing robustness, optimum, or playability claim is made.

Read [the report](../../Verification/dense-chains-probe-report.md) and [STATUS](../../Verification/DENSE-CHAINS-PROBE-STATUS.md) for conclusions and limitations. JSON is machine evidence; narrative documentation stays Markdown.
