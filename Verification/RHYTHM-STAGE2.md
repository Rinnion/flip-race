# RHYTHM-01 stage 2 — geometry and presentation checkpoint

19 October 2026. Author verification complete; browser check pending at this checkpoint. Human comfort is not established by the route scripts.

`TracksRhythm.cs` SHA-256: `8C77E83043D3A7CC3496B0206FD8D46DCFC9B0CD670F5527906F9C9890EF0E1C`.

## Physical course

The course is 4600 world units, with no rewards. Its ordinary and optional routes both finish in **20.909090909 s**. The initial pair demonstrates the two entry directions; the next six gates form the useful-action group.

| Family | X positions | Actual parts |
|---|---|---|
| Compact alternating offset | 300, 900, 1500, 1860, 2230, 2590, 2960, 3320 | width 16; upper Y0–110, lower Y130–240; offsets 0/112 or mirrored 112/0 |
| Straight | 3800 | width 8; upper Y0–12, lower Y228–240; both offsets 0 |
| Low floor threshold | 4300 | width 16; lower Y224–240 at X4300, upper Y0–114 at X4380 |

Every gate has two nonzero physical parts and one logical collision ID. The 360–370 pitch within the compact group replaces the previous 280–330 spacing; the previous recovery failure is preserved as historical evidence. Main's group has a surface jump plus an air correction per gate. Fast has one accepted surface press per gate with a real wall landing between them; removing any of these actions causes a hit. Fast's group intervals are 1.636–1.682 actual seconds.

The normal and quick-return witnesses share exactly this geometry. Fast normal enters the low gate from the opposite ceiling with one flip. Its optional route first establishes the floor, then uses one departure and one air return, ending on that same floor. Main normal uses its natural jump; its separate optional path uses two air corrections and also returns to its origin floor. Neither variant is silently substituted for the other.

## Reproduce and inspect

```powershell
dotnet run --project Tools/RhythmProof/RhythmProof.csproj --configuration Release -- --verify
dotnet run --project Tools/RhythmUiCheck/RhythmUiCheck.csproj --configuration Release
dotnet build Tools/FlightUiCompile/FlightUiCompile.csproj --nologo -v minimal
```

Author routes: **11,943 checks**, four complete routes × six dt, real supports, each normal input ±60 ms. Optional low Fast inputs each ±40 ms; optional Main inputs each ±30 ms. All author checks: **23,095**, including 98 actual first-collision states and 24 held-slow straight-gate games. Recovery checks preserve the remaining penalty, wait another 150 ms, then require the complete next gate to be physically cleared without another hit. This is a finite error sample, not proof for every input history.

Independent delivery audit at the same geometry hash: **9,584 assertions / 172 complete games**, own Main repeat .18 s and Fast quick-return .17 s; normal/optional windows, six dt and real landing checks pass. Independent sampled recovery: 98 states. Seventy-two bounded simple-controller bypass probes find no clean route. Separate authored band contacts cover 15 gates / 30 physical parts with **1,981 assertions / 180 contact games**. See `rhythm-stage2-independent*.txt` and the separate band-presence reports; these checks do not substitute for browser or human observation.

Exact browser inputs and tested timing margins are in `rhythm-stage2-routes.json`: profile `chosen`/preset 10 or `fast-flip`/preset 11, variant `normal` or `quick-return`. `rhythm-stage2-recovery.json` records each actual error state, target bound and rescue input clock. Runtime does not read these arrays to control either player.

The straight-gate Fast normal flip is **not feasible at held speed 110**. The separate same-wall air-return fixture succeeds on both walls and six dt, while the forbidden second repeat stays rejected. The fixture is marked as injected held-speed isolation, not a reachable stage reward history. Compact/low profiles and 110/220/330 transitions are covered in the engine author's separate geometry reports.

## Presentation

The existing 2×3 full-track page and seven-item previous-segment page remain. The stage card now says **«Проба ворот»**. The actual layout methods pass **258 checks** across 568×320, 844×390 and 1280×720. New physical-band/layout export: `rhythm-stage2-ui-layout.json`. Persistent `Application.version` still draws last beside the title, above the menu panel.

Rendering uses the actual band rectangles and their complete union for culling. The low family retains its visible threshold and no longer shows a one-direction crossing arrow: its hint acknowledges crossing or return to the same wall. This avoids misrepresenting the optional route. The normal compact gates retain their entry-direction cue. No diagonal collider or path is drawn.

All old/new finish zones now use a filled, outlined teal **22×16 px triangle**, whose tip is at the exact `(zone.X, zone.Y)` target; its body lies before the target in travel direction. The short ticks at the actual `FinishHeight` limits remain. The target has no animation displacement and is drawn before the mascot, so it cannot paint over the character. The existing world-X mirror plus upper-lane 180° transform is unchanged. No zone coordinate, height, 44-unit finish distance or 0.1-second reward changed. Browser checks must still confirm phone readability, approach, hit and miss in both lanes.

Compilation: complete Core/Presentation/Web .NET compile, **0 warnings / 0 errors**. Unity builds belong to root and were not launched by this agent.

## Preserved history

Original offset-192 snapshots: `rhythm-stage1-192-TracksRhythm.cs.txt`, `rhythm-stage1-192-proof.cs.txt`. Original `rhythm-stage-routes.json`, `RHYTHM-UI.md`, `rhythm-ui-layout.json`, `rhythm-offset-bands.svg` and stage-1 browser captures remain historical and are not claimed as verification of this corrected stage.
