# SCREEN-00 camera study

2026-10-21. Base gameplay: 4.7.0-trial. This report separates actual simulation replays, camera-only fixtures, and a geometric preview bound. No physics, profiles, track geometry, or production rendering was changed by this tool.

**Decision and production confirmation:** keep the old fixed horizontal scale, move the equal-player anchor from 19% to 25%, and cap the leader at 47% instead of 45%. Root accepted and applied the change. The final repeat calls production 25/47 directly and retains 19/45 as an explicit old-formula counterfactual of exactly the same states. Both captures agree on the numerical results below.

## Reproduction and source

```powershell
dotnet run --project Tools/SharedCameraCheck/SharedCameraCheck.csproj -c Release
```

- Tool: `Tools/SharedCameraCheck/Program.cs` and `README.md`.
- Final capture: `Verification/shared-camera-final.json` and `.txt`.
- Historical initial capture: `Verification/shared-camera-check.json` and `.txt`, preserved unchanged. It used production 19/45 and counterfactual 25/47.
- Result: **522,633 checks/observations,9 actual games**, plus pure camera fixtures and 108 geometric preview rows. The count includes per-step observations; it is not a count of independent games.
- Actual simulation is performed only through production `Press` and `Advance`. Both players have independent routes. Clones are used only to locate exact input distances.
- RaceSimulation SHA256:`AA91B96919AE318607D8BFF180AE7A54879FA54C2C6072003F5B245A57E10C24`.
- TracksMixed SHA256:`01C67639E567F2325D668678786AE5EAFDB5352C8204D8CC876EF288645210E0`.
- FlightSettings SHA256:`8499B6DC0D17A86A9CE02CE3F56FA4BD78D4F66CA1CB61E85B052C6BE5C9FB5E`.
- Initial SharedFieldView 19/45 SHA256:`2A8F0B613EB555162643AD062F57A31DAD31F17DC448CF0AEFB1A66D1A22ECC1`.
- Final production SharedFieldView 25/47 SHA256:`30B099264DD90BCF92CFA6835BE2C7D817B7C222A9EEC5602F0C563D9D6607A0`.

The final direct-production repeat passed the same 522,633 checks/observations and 9 actual games. It specifically tests the new 25% equal-player anchor,47% leader cap, and 47%-of-world-span trailing-centre exit after the cap is active. Core, profile and track hashes are unchanged.

The field widths 1190/826/550 are the existing logical arena widths at 1280×720,844×390,568×320. Horizontal results are identical at field heights 132.5 and 289. DPR affects the conversion to logical pixels, not this projection. Pure tests check equal X, scale, player identity, leader cap, true gap, no mutation, both leader assignments, the visibility threshold, and the finish bound.

## Actual clean pair: full 33.2 versus safe 36.0

Both profiles and both player assignments finish without collisions. The full runner earns nine ordinary rewards and the final dash. The safe runner earns none. Their true maximum gap is 616 world at the first finish;5 ms sampling reports 615.545–615.650.

| Viewport / field width | Main centre offscreen with 19/45 | Fast centre offscreen with 19/45 |25/47 on the same states |
| --- | --- | --- | --- |
|1280×720 /1190 |0 s |0 s |0 s |
|844×390 /826 |26.064–33.626 s, **7.562 s** |26.096–33.626 s, **7.530 s** |0 s |
|568×320 /550 |28.947–33.589 s, **4.642 s** |28.978–33.589 s, **4.610 s** |0 s |

These are centre crossings, matching the current helper. They are interpolated between samples at most 5 ms apart. A sprite begins clipping before its centre disappears. At 19/45 the leader reaches only 41.4%/41.1% on the two mobile sizes: the 45% cap has not caused this disappearance. It follows directly from the midpoint anchor 19%: the trailing centre exits at a gap of 38% of the visible world width.

The 25/47 projection leaves minimum sampled centre margins 18.23 px at 826 and 15.95 px at 550. At height 289 the existing body radius is 13.94 px, so the body remains inside. Decorative personal contours can extend farther; complete contour visibility is not asserted. The scale is unchanged.25% alone with cap 45% would leave almost no body margin at the larger mobile viewport.

The trailing runner returns after the leader physically stops atX7920, and both finish before the unchanged 3.5 s grace. The simulation does not pull either player forward. This scripted return uses the known route: it does not demonstrate that a human can navigate the hidden preceding obstacles.

## Preview at maximum ordinary speed

The calculation uses actual production `ObstacleGeometry.Right`, including the far half of offset and low gates. “Whole object visible” means its far physical edge is at or before `fieldWidth−20 px`; this 20 px is a readability margin, not a reward marker or a collision radius. The conservative leader position is the cap. Horizontal speed 330 is used to convert distance into the shortest ordinary-motion time;110,220 and recovery provide more time.

| Field width | Ahead time at 330,19/45 | Ahead time at 330,25/47 | Whole target visible before earliest needed preparation,25/47 | Spare after 150 ms reaction |
| --- | --- | --- | --- | --- |
|1190 |3.004 s |2.891 s |1.534 s |1.384 s |
|826 |2.190 s |2.107 s |0.749 s |0.599 s |
|550 |2.173 s |2.088 s |0.731 s |0.581 s |

The tightest preparation group in this conservative table is the straight gate: the safe witness starts its wall setup atX2560, and the complete gate ends at 3008, a 448 world preparation span. Other groups cover both small stones, the large stone, the low gate including both parts, the middle offset gate, the ceiling stone, the final offset gate, and the last low gate. Both profiles are included. The source captures all 108 rows, not just the minimum.

One published Fast safe action at 6080 is intentionally early preparation for the ceiling stone at 6800. Its full right edge 6864 would not yet be guaranteed visible at that unnecessarily early timestamp under a worst-case mobile leader cap. A separate actual production replay changes only that tool action from 6080 to 6500: both players still finish 36 s, zero collisions, zero rewards. The calculation uses this demonstrated later preparation for that group. No production route was changed, and the original early timestamp is retained in a separate diagnostic table.

This is a geometric notice bound and a legal baseline route demonstration. It is not a new held 330 physical route proof, a Cartesian input-jitter proof, or a human reaction test. The final dash reaches 440 only in the last 44 world, after the final physical obstacle; no new mandatory manoeuvre is introduced there.

## Real hit, lead change, and very large gap

For each profile, omitting the first small-stone manoeuvre on the full route causes exactly one actual collision and loses that pad reward. The runner then overtakes the safe player through later actual rewards and finishes 34.0 versus 36.0. There is one genuine leader change; neither camera changes player identity or physical progress. Both centres stay visible under either framing in this case.

For each profile, full versus no action causes eight actual collisions for the idle player. Maximum gap is about 1496 world. The winner finishes 33.2; the game stops at the exact normal grace deadline 36.7; the other player is atX7194 and has not finished.

With 25/47 the mobile lagger is still outside at the deadline:844 exits about 15.309 s and 550 about 15.355 s, leaving about 21.4 s outside. On 1280 it exits about 19.29 s and re-enters about 36.03 s, shortly before grace ends. These are not synthetic speed or position fixtures. The marker can report the true gap, but cannot show the hidden obstacles. Large-gap blind play remains an explicit limitation of this trial.

The shared camera remains bounded by `Length−AnchorX/ScaleX`; the first finished player stays at the real finish. Until the normal game phase becomes finished, the second player continues advancing in the same world. UI result-overlay timing still requires the parent's actual browser check.

## Read-only artwork review

The shared artwork iterates the physical obstacles once, uses the union bounds for culling and each actual gate band for drawing. A personal collision does not hide physical geometry. A landing rail remains visible after personal rewards are consumed. A reward remains bright/visible while available to the other player; pending state does not produce a success event. No actionable inconsistency was found in this source read. Raster readability, overlap and touch behaviour remain browser/human checks.
