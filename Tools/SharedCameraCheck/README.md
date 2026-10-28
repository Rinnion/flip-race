# Shared camera check

Run from the Unity project directory:

```powershell
dotnet run --project Tools/SharedCameraCheck/SharedCameraCheck.csproj -c Release
```

The default output is `Verification/camera481-check.json`; an optional first argument selects another path. Preserve the historical `shared-camera-final.json/.txt` and earlier 19/45 checkpoint.

The production camera uses `anchor = clamp(width * .19, 62, 195)`, the unchanged legacy horizontal scale, and `cameraLeft = min(distance0, distance1) - anchor / scaleX`. There is no start/finish clamp, midpoint, cap or zoom. The lagger stays at the anchor; the leader may exit right.

Synthetic projection fixtures are labelled and separate from gameplay witnesses. They cover equality, either lagger, start/finish boundaries (including out-of-track projection-only positions), continuous overtake, actual right-edge threshold and return, both heights, fixed scale and complete Core state fingerprints.

Actual games use production Press/Advance and authored independent distance inputs on the unchanged mixed course. They cover both player assignments for full/safe and full/no-action, real eight-hit large gaps, one-hit overtakes, personal finishes and normal finish grace. Snapshot bisection locates inputs without injecting predicted state. Every100 samples, complete serialized Core fingerprints (including track, settings, effects and personal sets) are compared before and after projection.

Offscreen means the leader centre is right of the field, matching production IsPlayerVisible. Crossings are interpolated between samples no more than 5 ms apart. Large-gap fixtures assert actual exit/return and a finished offscreen leader while the lagger still races. Clean full/safe runs remain visible at the sampled widths. Actual 1190-wide no-action runs need not exit: the narrower 826/550 fields provide the real exit witnesses.

Counters include repeated observations. The tool does not draw Unity pixels, deliver native input, or establish human playability. JSON Inputs contain timestamped native replay schedules; times start at racing elapsed0, after the normal countdown. Choose `miss first small then full/safe` for an overtake, `full/no-action` or `no-action/full` for a large gap.


The one-hit comeback timing assertion follows the accepted 4.9.1 chevron contact geometry: both historical route schedules earn exactly eight ordinary rewards, hit one obstacle, and skip the optional finish target. Their finish is `36 - 8 * 0.3 + 0.5 = 34.1` seconds. The older assertion also subtracted the 0.25-second dash despite no contact. V5 investigation reproduced that failure on isolated, unmodified `a235d398db1fb1c79603d68a973f451a098a520b` Core, view, harness and checker sources. Baseline evidence and diagnostic counters are in `Verification/v5-camera-baseline.txt`; all original projection/gameplay checks remain, with explicit ordinary/finish reward counts added. Precise aimed finish-target collection remains covered by Fix491 proof and the Core finish suite.
