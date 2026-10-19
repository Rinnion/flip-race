# TwoModesProof — MODES 4.5

All routes use production `RaceSimulation.Press` / `Advance`. Distance inputs account for mid-step reward speed changes through snapshots of production simulation. Fast inputs must start from a real support; no coordinates, velocity, readiness, effect history or support are injected.

Run from the Unity project root with .NET 10:

```powershell
dotnet run --project Tools/TwoModesProof/TwoModesProof.csproj
dotnet run --project Tools/TwoModesProof/TwoModesProof.csproj -- --verify
dotnet run --project Tools/TwoModesProof/TwoModesProof.csproj -- --windows
dotnet run --project Tools/TwoModesProof/TwoModesProof.csproj -- --hover
dotnet run --project Tools/TwoModesProof/TwoModesProof.csproj -- --routes
dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- --modes
pwsh -NoProfile -File Tools/TwoModesProof/rollback-open-gate.ps1
```

- Default: common short stage, both exact presets, six step sizes, safe ±60 ms, narrow support and natural walkoff, chain both/each independently.
- `--verify`: all three shared tracks, safe/full/negative, exact physical finishes, empty effects at the finish plane, safe ±60 ms / full ±25 ms actual-clock shifts, and the universal geometric tail bound for all 2048 reward subsets.
- `--windows`: finite single-input timing windows around the full route, 5 ms scan followed by bisection of the first failing boundary. Outputs `Verification/modes-windows.json`; each window changes one action and keeps later actions distance-driven.
- `--hover`: immutable pre-MODES geometry from git commit39097d0, including all three tracks, with the former real Long hover reproducer. New confirmation must already have happened before the hovering; no source or state substitution is used.
- `--routes`: `Verification/modes-routes.json`: both profiles × practice/three full tracks, safe/full/negative X arrays, all three actual input clocks, exact profile values and shared geometry.
- Rollback: makes an isolated temporary build with only the absent-gate-half guard removed. Expected RED is the real wall-run collision. Production files are not changed.

`--draft`, `--draft-full` and `--search-short` are offline design diagnostics, not additional runtime modes. Recovery and independently scheduled replays are in `Tools/TwoModesAudit`; do not reinterpret those sampled recoveries as proof of every possible mistake. See `Verification/MODES-CORE.md`, `MODES-GEOMETRY.md` and `MODES-AUDIT.md` for scope and hashes.
