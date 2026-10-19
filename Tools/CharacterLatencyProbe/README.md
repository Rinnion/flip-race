# Character selection CPU latency probe — diagnostic only

19 October 2026. This probe does not edit or rebuild the game. It extracts the current `SelectCharacter` method, adds timestamps immediately after `SetCharacter` and `GetFrame` in that isolated copy, and executes it with unchanged production Core, `CharacterRoster` and `MascotArtwork` sources.

Unity vector/math/texture operations are explicitly substituted in `UnityShim.cs`. Rasterization, frame generation and cache selection are real production code. `SetPixels32` clones its array; GPU upload, Unity native allocation/destruction, browser event dispatch, audio and painting are not measured. .NET desktop timings must not be presented as WebGL or phone timings.

## Reproduce

From the project directory in PowerShell:

```powershell
.\Tools\CharacterLatencyProbe\Run.ps1 -ReportName report.json
```

The command uses the installed .NET SDK at `C:\Program Files\dotnet\dotnet.exe`, Release configuration. A diagnostic 100 ms synchronous-selection budget produces exit 1 if exceeded. This is an explicit investigation threshold, not an established product SLA. Source hashes are stored beside each report. All writes stay in this diagnostic directory or its build directories.

Startup mirrors the two initially selected animation variants plus eight menu previews. Then three rounds select all four characters on both sides, immediately repeating the current selection after every action. A later revisit after selecting another character is distinct from repeating the currently selected character.

## Already-run results

| Run | Changed selections | Min / median / max synchronous return | Repeat-current median | Maximum state update |
|---|---:|---:|---:|---:|
| `report-first.json` | 23 | 305.48 / 341.83 / 402.33 ms | 0.0017 ms | 0.1728 ms |
| `report-repeat.json` | 23 | 321.02 / 355.46 / 465.80 ms | 0.00185 ms | 0.1994 ms |

Both returned `RED_CPU_SELECTION`, exit 1. Each changed selection created 192 frame textures in the shim; repeat-current calls created zero. Revisit-after-other calls reproduce the slow path. Every selected character state was correct. The first selection of slot 0 / hero 0 is already warm at startup, hence 23 changed selections rather than 24. The four measured production source hashes remained unchanged after the runs.

This initial investigation demonstrated a repeatable long synchronous CPU segment at the actual selection seam. It did not by itself prove the size of the visible browser delay, a regression from enlarged cards, or a specific user's device timing. Implementation subsequently added lazy pose preparation; the same probe's [root baseline](report-root-baseline.json) and [root lazy result](report-root-lazy.json) measured changed-selection medians of 331.1655 and 2.7650 ms. The original baseline JSON and probe/shim code are preserved.

## Browser boundary

In the original analytical chat, the attempted browser opening returned `Browser is not available: iab`, and the surface inventory was empty. That limitation applied to that investigation context. The implementation chat has IAB access and has now measured real before/after pointer events, public state publication, rAF gaps, reload/startup and screenshot-confirmed visuals. See [MODES-LATENCY](../../Verification/MODES-LATENCY.md) for results and raw artifacts.

The read-only DOM bridge exposes `dataset.flipState` (`characterRects[player][hero]`, character and readiness) and `dataset.flipReportedAt`. The implementation also captured native `PerformanceEventTiming.duration`: next-paint latency with 8 ms granularity, distinct from callback time, matching public-state publication and the two-rAF proxy. Native timing is the primary browser latency result; publication is sampled and the second rAF is not exact first presentation. Screenshots establish that correct pixels were visible by capture time. CPU `GetFrame` timings remain separate from all browser measurements. The comparison uses the preserved 4.4 review and its lazy-pose successor on the same desktop IAB; it does not establish that larger cards caused the delay. Physical-phone timing remains untested. Full definitions, native samples and caveats are in [MODES-LATENCY](../../Verification/MODES-LATENCY.md).
