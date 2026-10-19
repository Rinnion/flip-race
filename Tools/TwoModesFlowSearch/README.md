# Historical FastFlip route search for Flow Y180

**Historical checkpoint, not the final runtime proof.** After this search the authorised final geometry changed Flow's finish target from Y180 to Y208 and added a supported setup before the finish. The original arrays and export below are deliberately preserved; `--verify` against the final geometry is not expected to pass. Do not use this tool's 5,220-check result or ±8.4 ms finish window for the final release. Use `Tools/TwoModesProof`, `Verification/modes-routes.json` and `Verification/MODES-RHYTHM.md` for the final geometry.

19 October 2026. Tool-only route construction; no changes to gameplay files. Uses production `RaceSimulation.Press/Advance`, `TracksFull.Get(full-flow)` and the shared deep-copy/scheduler helpers from `TrackProofControl/Harness.cs`. Every FastFlip press must occur on a real support; there are no hidden air inputs or substituted motion equations.

```powershell
dotnet run --project Tools/TwoModesFlowSearch/TwoModesFlowSearch.csproj -- --verify
dotnet run --project Tools/TwoModesFlowSearch/TwoModesFlowSearch.csproj -- --export
```

Historical checkpoint result: **5,220 checks PASS**. The six maximum integration steps are 1/30, 1/60, 1/144, .317, 2.5 and 100 seconds. The scheduler splits at input distances and uses production advancement to resolve any changing speed.

| Route | Inputs | Ordinary positive | Finish | Negative | Hits | Finish time |
|---|---:|---:|---:|---:|---:|---:|
| Safe | 5 | 0 | 0 | 0 | 0 | 36.0 s |
| Full | 20 | 9 | 1 | 0 | 0 | 33.2 s |
| Trap | 21 | 9 | 1 | 1 | 0 | 33.5 s |

Both players execute the same inputs; exact ties and the complete ordinary-effect ledger are checked. Every earned ordinary effect has finished, with no active or queued effect at the tape. The trap route deliberately takes the second trap and reaches the final plane from the opposite wall, so its final departure differs from the full route.

All five safe inputs separately tolerate ±60 ms of actual input-clock shift. Every ordinary full-route input separately tolerates ±50 ms. Other inputs retain their exact distance schedule; these checks do not claim arbitrary combinations of timing errors. The final full-route departure separately tolerates ±8 ms. Its measured local interval is approximately **−8.344/+8.548 ms**, narrower than ordinary actions because FastFlip crosses the 16-unit finish target at high vertical speed. This limitation was explicitly reported; the target and physics were not widened here.

`Artifacts/flow-routes.json` contains numeric safe/full/trap arrays, actual full input timestamps and individual local window estimates. Window searches are capped at ±150 ms, so values at that cap mean at least that much tested allowance, not a measured outer boundary. The closest ordinary interval is the second chain action at x3450, approximately −58.618/+61.419 ms; the middle passage at x2854.0203593 allows approximately ±63.157 ms.

The first investigation used a tool-only copy of proposed gate geometry and exposed collision against a zero-height gate half at a wall edge. The production author fixed that case and integrated the common gate geometry. **The final tool has no gate projection or surrogate:** it reads production Flow directly. All rings and landing stones remained unchanged during this search.

Checkpoint source hashes (the track author may subsequently edit other tracks):

- `TracksFull.cs`: `D2D15134427B7C05C0E8E01F29A51AD69247F7A8343B1144B49D7AB3F0C23212`
- `RaceSimulation.cs`: `CFA0A419C9CE9FC7C0F2A7F075CACC8E7CD3C9655D12412037AA2E5D4BC7838B`
- This tool's `Program.cs`: `DF82654DF340E5F11B79F01E784979C7F351C334CCC29AB04BDAECDC98F89576`

This is an isolated Flow route check, not a browser or physical-phone result and not the final combined three-track freeze.
