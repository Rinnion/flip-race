# Independent TRACKS audit

Actual `RaceSimulation.Press`/`Advance`, separate analytic oracles, no Unity API or runtime changes. [Plan](../../Verification/TRACKS-AUDIT-PLAN.md), [stage-one report](../../Verification/TRACKS-01-AUDIT.md).

TRACKS-01 **PASS: 58 790 assertions /288 games**. This includes many intermediate advancement checks, not 58 790 unique scenarios. Full-track/browser/release results are not implied.

Run in a fresh process from the Unity project root:

```powershell
pwsh -NoProfile -File Tools/TracksAudit/Run.ps1
# Alternative, with .NET10 SDK:
dotnet run --project Tools/TracksAudit/TracksAudit.csproj
```

The mirror reaches the ceiling using actual chosen Jump and an air repeat at its analytic apex. No player coordinates, speed, charge, support, reward or measurement is injected. Horizontal target scheduling uses a conservative upper speed bound440 and converges from below; it does not call production contact predictors.

Constant330 fixtures obtain three actual co-located instant rewards before the jump so boost lasts through the flight. A width700/window400 synthetic marked stone tests genuine second landing after a350 jump; this does not claim an authored64 stone can be revisited before its trailing edge. Authored Practice is independently tested at its real64/32 geometry.

Cases: chosen profile and unchanged old presets; six dt values through100; both walls and both players; ordinary one-jump64×48 versus64×92/200×92 at220/330; marker endpoints and side impact; press immediately before/after edge764; natural150ms after landing; safe±60ms; full/first-only/second-only chain, real finish, completed pulses and empty queue.

The [full-track report](../../Verification/TRACKS-FULL-AUDIT.md) records the frozen geometry D3B02858: 217 route/window games, 124 local safe ±60ms cases, 263 sampled collision-recovery states and 36 first-only/second-only chain branches. It also preserves a real delayed-after clean-tail counterexample; the release decision remains open.

Run the full checks in another fresh process:

```powershell
$sources = @(Get-ChildItem Assets/Scripts/Core/*.cs | ForEach-Object FullName) +
           @(Get-ChildItem Tools/TracksAudit/*.cs | ForEach-Object FullName)
Add-Type -Path $sources
[TracksIndependentAudit]::RunFullProof()
[TracksIndependentAudit]::RunFullRecovery()
[TracksIndependentAudit]::RunFullChains()
# Diagnostic counterexample, not a new-contract PASS assertion:
[TracksIndependentAudit]::RunAfterPendingTargetProbe()
```

Saved standalone source and stdout are linked from each report. Collision recovery is a finite sample of individual timing errors, not every possible history. The chain-only branches stop after both contact bands, rather than claiming a full alternate route through all later obstacles.
