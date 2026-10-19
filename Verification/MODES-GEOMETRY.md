# Two shared controls — final geometry, 2026-10-19

Both named controls use identical geometry through `TracksFull.Get(id, flight)` / `Practice(flight)`; custom settings also use this geometry. Only the two exact presets have authored route guarantees. `FlightPresets.Chosen` remains Jump350/g662.5/unlimited; `FlightPresets.FastFlip` is Flip/g2700/impulse35/no repeats, with inactive values300/530/112 preserved. No simulation parameter was changed to make a route pass.

`SafeRoute(id, flight)`, `FullRoute(id, flight)` and `NegativeRoute(id, flight)` provide actual distance actions. Legacy overloads and `Track.SafeRoute/RiskRoute` retain the Chosen paths. Browser export: [modes-routes.json](modes-routes.json), eight entries, exact profile values, X arrays and safe/full/negative actual input clocks.

## Shared short stage

Length3300, safe15.0s / full13.5s. It contains a middle passage, one64×48 marked stone, two independent instant rings and linked before/after rings. Both profiles collect all5 positives without an impact. Fast uses seven real supported departures and zero airborne actions. The marked landing atX732 naturally leaves the64-unit face after32/330s; 150ms after landing the character is falling safely without a rescue tap. Both chain rings and each one alone have actual-action witnesses for both profiles.

Author stage result: **2158 assertions PASS**, steps1/30,1/60,1/144,.317,2.5,100, safe actions shifted individually±60ms. Independent stage scope is in [MODES-AUDIT.md](MODES-AUDIT.md).

## Three full tracks

Each shared track has length7920,9 ordinary positives,2 negative rings and the separate last44-unit finish target. Safe36.0s; all ordinary positives plus finish33.2s; full route with one intentional negative33.5s. Both players, both profiles and all six step sizes pass. The finish-only comparison is33.2vs33.3s; the new target remains a16-unit center-Y window and uses440 speed until the tape.

Changes to the old geometry are limited to:

- Flow's near-wall gates reach the corresponding wall (`114..240` or `0..126`). Its middle gate uses gap`12..228`, width8. No zero-height half is collidable.
- Short's early negative moves fromX1800 to1790; other coordinates and its radius are preserved. This widens the optional Fast low-ring timing without changing the Chosen series.
- Finish centers areY32 for Long/Short andY208 for Flow. Fast explicitly reaches the appropriate wall before its last departure. Input times are centered across the target's vertical window. The physical X7876, height16, speed440 and actual0.1s reward are unchanged.

All stone sizes, narrow64×48 landings,200×92 large platforms and ordinary positive-ring geometry on the full tracks remain shared. The primary Long5|6|5 and Short3|3|2|3|3|2 maneuver organization is retained. Fast naturally uses a different input rhythm: full18/18/21 actions; safe2/4/5. Its safe Long route has a long wall-running tail. These are explicit differences in how the two physics models traverse the same world, not equivalent copies of the primary action grouping. See [MODES-RHYTHM.md](MODES-RHYTHM.md).

Author full proof: **48596 assertions PASS**. Every safe action was shifted individually±60ms; every full action, including finish,±25ms. Exact measured local windows are in [modes-windows.json](modes-windows.json). Minimum symmetric full window on each track: **Chosen25.508ms; Fast26.172ms**, both set by the final target. Later actions remain distance-driven during these individual timing tests. This is a local input window, not a claim that arbitrary simultaneous jitter or every possible mistake is safe.

Independent proof covers six-step routes,146 safe timing shifts, both finish-only winner assignments and370 sampled recovery states. Recovery means an actual collision, its remaining penalty plus150ms, then a reachable continuation without the next obstacle causing another impact. See [MODES-AUDIT.md](MODES-AUDIT.md).

## Universal clean finish entry

For a circle, the latest possible contact is `X+Radius+14`; for a landing, `min(stone.Right, X+LandingWidth/2)`. A linked ring's latest confirmation is the maximum of its latest contact and `max(ClearX, stone.Right+14+1e-7)`. Removing the extra landing wait makes every such bound finite.

Measure remaining ordinary effect work in distance: positive198units, negative66units per full pulse. While work exists, `dW/dX=-1`; recovery freezes both X and work. Instant preemption and extension preserve total work. Sort latest release positions U and compute `F=max(F,U)+weight`. Delaying a release to its latest bound cannot improve the worst completion; skipped/cancelled rewards only reduce work. The tool additionally checks all2048 subsets of the11 ordinary positive/negative opportunities.

| Track | All ordinary effects ended by X | Margin before7876 |
|---|---:|---:|
| Long |7688 |188 |
| Short |7728 |148 |
| Flow |7688 |188 |

This covers any history that reaches the finish plane, including prolonged airborne motion, impacts and missed rewards. It does not depend on the ideal route. No queue is cleared artificially. Therefore collecting the final target on these tracks produces a genuine additional0.1s gain.

Frozen TracksFull SHA-256: `717C0BE9560498FFABFEF56185A235B4F76EE92D9B4373173EF3AFAFA67E1861`. Core and compatibility details: [MODES-CORE.md](MODES-CORE.md). This report covers engine/geometry proofs; final real-browser input and rendering are recorded separately.
