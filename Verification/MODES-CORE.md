# MODES core verification — 2026-10-19

Runtime is frozen for 4.5.0-trial. This report covers .NET engine verification; the final WebGL/browser checks are reported separately. Earlier 4.4 reports and the original RED evidence are preserved.

## Two intentional changes

1. A collected linked ring now confirms at `max(ClearX, obstacle.X + obstacle.Width + PlayerRadius + 1e-7)`, including an after-ring. Only the additional wait for a landing was removed. A linked impact before clearance still cancels an already pending ring; collision-first ties, later collection after a historical hit, and previously earned rewards are unchanged. Late collection past the physical boundary confirms immediately. No deadline, timeout or forced effect reset was introduced.
2. A gate gap reaching a wall has no solid half at that wall. `NextCollision` skips the upper band when `GapTop <= 0` and the lower band when `GapBottom >= CorridorHeight`. A nonempty band, including height1e-6, still collides; nonempty corner contacts retain their inclusive continuous test.

The second change was needed for the common geometry specified in the audit: the old zero-height half caused an actual collision for a character running along an apparently open wall. The isolated rollback script removes only the guard and reproduces `EXPECTED RED: open gate wall-run has no absent-half collision`. Production sources are not modified by the rollback.

## Tests and compatibility

`EngineModesTests` is included in `EngineParityTests.Run`, the Unity menu/execute method and `Tools/ParityRunner`. New coverage: both presets and both walls, six step sizes up to100s, zero/nonempty halves, exact corner and ±1ms alternatives, too-early ClearX, physical trailing-edge cancellation and airborne confirmation. Result: **3 scenarios / 612 assertions PASS**.

Historical expectations that explicitly required after landing were intentionally updated in `EngineV3Tests.AfterRingConfirmation`, `EngineV4Tests.LinkedGravityReturn` / post-hit confirmation, and the CONTROL linked-ring/landing fixture. In the latter, the earlier real boost now moves the arrival point; the fixture uses a wider marked face and verifies the partly served linked boost is extended at the actual landing time. Real collision-cancellation tests remain and pass. No JS fixture or comparison exception was changed.

| Suite | Assertions / result |
|---|---:|
| Original V1,13 scenarios |963;186 sampled recoveries |
| V2,12 scenarios |3554;449 sampled recoveries |
| V3,12 scenarios |865 |
| V4,18 scenarios |1770 |
| TUNE,13 scenarios |1009 |
| CONTROL,19 scenarios |2166 |
| Finish,10 scenarios |1598 |
| FLIGHT,12 scenarios |1592 |
| MODES,3 scenarios |612 |
| Unchanged JS differential |40228 snapshots /1448208 fields, tolerance<1e-8 |

Full stdout: [modes-regression.txt](modes-regression.txt). Commands:

```powershell
dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- Tools/engine-parity-fixtures.tsv.gz
dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- --modes
pwsh -NoProfile -File Tools/TwoModesProof/rollback-open-gate.ps1
```

## Historical hover regression

The entire old geometry is preserved from git commit`39097d0379d5a2e33ad531383cdd641e9b5a81f2` in `Tools/TwoModesProof/Snapshots/LegacyFullTracks.cs` (original TracksFull SHA`D3B0285856E2D7A45284435A5AD5784850F6713DA1643B961CBBD9D1CDEF7A8D`). On that unchanged geometry, the real Chosen route collects the after-ring, performs41 accepted airborne switches, lands and takes the old finish target. It now has all9 ordinary impulses fully served at X7876 and finishes33.2s without an impact. Steps1/144,.317 and100 pass: **3144 assertions**. This is a new GREEN report; it does not replace `TRACKS-LONG-TAIL-RED.txt`.

## Frozen hashes

| File | SHA-256 |
|---|---|
| RaceSimulation.cs |`CFA0A419C9CE9FC7C0F2A7F075CACC8E7CD3C9655D12412037AA2E5D4BC7838B`|
| FlightSettings.cs |`D45405A1F1C5530FB41E3208C23A563E5C663C3226668E447706C73C2B03DF95`|
| EngineModesTests.cs |`943465D84EB4B9968D85400DACA917FF42D281645BF88009033685CC791F7897`|

Independent semantics, gate edges and route/recovery evidence: [MODES-AUDIT.md](MODES-AUDIT.md).
