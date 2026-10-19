# MODES-01 lazy mascot artwork — independent check

19 October 2026. **PASS: pixel output and public pose selection remain unchanged.** No game Assets or original `Tools/CharacterLatencyProbe` files were edited for this check.

Baseline: commit `39097d0379d5a2e33ad531383cdd641e9b5a81f2`.

Current mascot SHA-256: `4EAA74E9449E75749CDCD2DE1B4D5F0B1B479EDFF4F3727BBFF2643D12F3998E`.

Roster SHA-256: `070E92E3B03A4C7AB9D980CFEEC05229FF086CE2EA85094F49D2FA99926523DB`.

## Pixel and cache equivalence

The isolated `Tools/MascotPoseCheck` loads both source versions under different class names. Each texture is hashed over its actual ordered RGBA pixel bytes before the shim discards readable data. All four heroes and both scarf/player variants are included.

- **1,536 / 1,536 animation poses match**: 96 run, 16 run-blink, 16 run-success, 12 air, 12 air-blink and five eight-frame detail/reaction sets per variant.
- **8 / 8 previews match**.
- **2,048 public motion/reaction selector comparisons match**, exercising clocks, flight progress and state-priority branches.
- **1,536 repeated-pose calls reuse the same texture and create zero additional textures**.

The diff changes preparation/cache timing, leaving path drawing, pose parameters, stepping and scarf colors intact. Retaining six supersampled body/eye layers per selected character adds about 3 MiB of managed pixel buffers for the two active variants once both sets exist. Completed frame caches still have the same maximum texture count. One debug texture name loses the `blink` suffix; pixels and selection behavior are unaffected.

## CPU measurements

Release .NET 10 desktop runner with the same Unity math/texture shim for both versions. SHA capture is **disabled** during timing. Startup includes initial JIT/static initialization; the same ten application requests run in separate fresh processes. These are synchronous CPU timings, **not WebGL input-to-paint or physical-phone measurements**.

| Operation | Eager baseline | Lazy current |
|---|---:|---:|
| Startup: two selected idle variants + eight menu previews | 744.847 ms; 392 textures | 42.310 ms; 10 textures |
| Startup allocated bytes in shim | 36,196,728 | 8,784,144 |
| Change selected variant, median / max, 23 changes | 326.640 / 350.977 ms | 2.563 / 4.287 ms |
| Repeat current selection, median, 24 repeats | 0.00095 ms | 0.00070 ms |
| First request of a running pose, median / max, 192 requests | 0.00010 / 0.0393 ms; already baked | 1.857 / 3.347 ms; one texture per request |
| Repeat same running pose, median, 192 repeats | 0.00010 ms | 0.00030 ms |

Repeated selection and pose lookups created zero textures in both implementations. Each changed selection previously created all 192 poses; the lazy implementation creates one requested idle pose. Later uncached animation poses now incur a small CPU rasterization cost on first use. The browser must measure whether that first-use work fits its real frame budget; this report does not assume .NET timing transfers to WebGL.

## Reproduction and limits

```powershell
.\Tools\MascotPoseCheck\Run.ps1
```

Raw artifacts: `Tools/MascotPoseCheck/sources.json`, `compare.json`, `perf-old.json`, `perf-new.json`. Both source snapshots and the dedicated shim are kept alongside the runner. Compilation passed with zero warnings/errors.

This check confirms unchanged raster output, cache behavior and reduced synchronous CPU preparation. It excludes actual Unity GPU upload/destruction, browser event dispatch, animation-frame scheduling and first paint. Parent-task browser latency/startup evidence is still required before closing MODES-01; no physical-phone result is claimed here.
