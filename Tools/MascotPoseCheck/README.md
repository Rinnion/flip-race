# Mascot pose equivalence and CPU cache check

Run from the Unity project directory:

```powershell
.\Tools\MascotPoseCheck\Run.ps1
```

The runner reads `MascotArtwork.cs` from commit `39097d0` and the current workspace, changing only each copied class name so both versions coexist. It uses the production `CharacterRoster`. No game file or `Tools/CharacterLatencyProbe` file is modified. Generated copies and results stay in this directory.

The Unity shim is copied from `CharacterLatencyProbe`, with optional pixel retention and SHA-256 over ordered RGBA bytes. Capture is disabled in performance runs. Both versions use the same math/raster/texture-copy substitutes. GPU upload, Unity native allocation/destruction, browser input, first paint and physical-phone performance are excluded.

Three fresh processes produce:

- `perf-old.json`: eager baseline, startup and first/repeated lookup timings.
- `perf-new.json`: current implementation under the same sequence.
- `compare.json`: every pose and preview hash, plus public-selector and cache checks.

Coverage is 8 hero/player variants × 192 poses, 8 previews, 2,048 motion/reaction public-selector comparisons, and 1,536 repeated-pose identity checks. Private frame arrays are inspected only in this isolated harness to ensure every pose is compared, including blink/reaction states that ordinary play might not visit.

Startup consists of the two selected idle frames and all eight menu previews. Afterward, three rounds select every hero on both sides, immediately repeat the selection, then request eight new running poses and repeat each. Of the 24 selection requests, the first startup-selected character is already cached; the other 23 create a new active variant. Report selection-change medians from those 23, not as 24 cold selections. First-running-pose requests are already baked in the old implementation but individually rasterized in the new implementation; this is the expected tradeoff, not a regression in cached lookup.

19 October 2026 result: **1,536 poses + 8 previews pixel-identical**, all selector/cache checks passed. See `Verification/MODES-ARTWORK.md` for measured numbers and source identity.
