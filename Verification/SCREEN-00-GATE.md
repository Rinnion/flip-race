# SCREEN-00 → SCREEN-01

2026-10-21. Baseline `fc67c90` / 4.7.0-trial. Stage WebGL `4.8.0-stage1`, `FLIP_SCREEN_STAGE`, unchanged full Mixed track; the first 900 world units serve as the risky fragment. The longer run supplies a real large gap rather than injected distances.

The 568×320 browser run used native A/L presses only: Fast full witness for player 1, no actions for player 2, same hero 0/0, shared opposite / Half. `screen-stage1-fast-asym.json` contains inputs, published read-only state, and screenshots. No pauses or recovery clicks occurred. Player 1 finished at 33.3 s with zero hits, seven instant rewards and nine ordinary rewards; the finish target was missed. Player 2 had eight hits and did not finish before the existing grace timeout. This is a rendering/contact check, not an ideal-route proof.

Observed: one world; exactly coincident start coordinates and both identity contours; the first landing rewards only its owner; the other marker and physical stone remain; actual separation and a real offscreen trailer; the first finisher does not cover the track while the second continues. See `screen-stage1-fast-asym-{start,hit,personal,offscreen,firstfinish,finish}.png`.

The stage revealed/confirmed follow-up presentation fixes: racing status bars occupied the middle of the Half field; texture halves applied too broadly during near-overlap; the upper UI reversed its direction arrow. Source corrections move racing status bars to the outer edges, restrict half textures to <0.5 px coincident origins, and put the offscreen pointer in the world orientation. Their pixels are checked in SCREEN-03.

Independent gates: `SCREEN-00-RENDERER.md`, `SCREEN-00-CAMERA.md`, SharedScreenCheck and managed production-handler checks. Core/contact tests passed. Camera 19%/45% hides clean full/safe runners for 4.6–7.6 s on phones; accepted trial correction is 25%/47% with identical horizontal scale. Nine actual paired replays confirm clean full/safe visibility and adequate mandatory-obstacle notice. An eight-hit deficit can still hide the trailer until the ordinary timeout; the indicator does not fix that limitation.

Decision: proceed to full personal-state/geometry acceptance (SCREEN-01). No Core or track edits. The stage-only default and version are not the release configuration.
