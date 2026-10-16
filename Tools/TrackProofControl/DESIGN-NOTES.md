# CONTROL geometry preparation

Historical preparation notes, written before root's CONTROL-02 and free-browser approval. See [REPORT.md](REPORT.md) for the final authored geometry and measured results. Old TracksV4 and TracksTune remain unchanged.

## Fixed parameters for the comparison

Player radius is 14; the corridor is 240 high, so wall-center travel is 212. Ordinary horizontal speed is 220, boost is 330 and slow is 110.

| Mode | Profile | Initial movement | Air input |
| --- | --- | --- | --- |
| GravityAirFlip | Moderate, g = 1000, u = 70 | Opposite gravity and impulse 70 | One acceleration reversal, preserves velocity |
| LinearAirFlip | Baseline is immaterial | Constant transverse speed 265 | One immediate velocity reversal |
| JumpGravityFlip | Jump, g = 662.5, u = 400 | Jump away, gravity stays toward support | One acceleration reversal, preserves velocity |
| GravityUnlimited | Moderate, g = 1000, u = 70 | Same first launch as GravityAirFlip | Every separate press reverses acceleration |

Analytic unboosted vertical timing, used only as starting estimates:

| Motion | Time, seconds |
| --- | ---: |
| Moderate full wall transfer | 0.584904573 |
| Moderate opposite-wall approach to low 48 stone | 0.506974869 |
| Moderate opposite-wall approach to high 92 stone | 0.424873721 |
| Linear opposite-wall approach to low 48 stone | 0.618867925 |
| Linear opposite-wall approach to high 92 stone | 0.452830189 |
| Jump apex | 0.603773585 |
| Jump return to original wall | 1.207547170 |
| Jump descending to low 48 stone | 1.072427915 |
| Jump descending to high 92 stone | 0.898403340 |

The jump rises 120.754717, leaving 28.754717 of vertical margin above a 92 stone. This establishes height plausibility only: its horizontal approach must also clear the expanded leading side. Any air reversal changes the later values; a stone departure also starts at its actual plane, never the wall.

The opposite wall is not automatically safe during a normal hybrid jump. Starting from floor, the apex center is 105.245283, while a ceiling stone of height 92 has its expanded contact plane at 106. Their bands overlap by 0.754717. A high ceiling stone above the apex can therefore cause a real hit even without a gravity reversal. The mirrored constraint also applies. The route proof must check all obstacle bands, not just the stone being intentionally jumped onto.

## Small landing checkpoint

The existing tool-only fixture has a 180-wide stone, marker width 44 located 50 after its leading edge. It is intentionally wider than the old 64-wide stones, because landing starts the reward immediately. Even the latest legal landing leaves 108 to the trailing edge: at 330 this is 0.3273 seconds of supported run. Early departure and deliberate walkoff are separate branches.

At speed 330 a 44-wide marker corresponds to a total horizontal crossing span 0.1333 seconds. This is not a promise of a ±66.7 ms input window: changing an input affects vertical contact time, later rewards and further actions. The proof will perturb actual input time while simulating production motion, report the measured one-input windows and identify any limiting edge. Target useful individual tolerance is at least ±40 ms; larger windows are preferable. Do not expand the rendered marker beyond the legal center-X interval.

Use this order: one floor landing, its exact ceiling mirror, each mode's departure and boosted edge walkoff, then the common comparison. The engine already owns detailed boundary/collision tests; this tool owns complete route reachability and played timing.

## Comparison route concept

Aim for 18–21 seconds at ordinary speed, with about 4 seconds before the first obstacle. Two marked stones, low 48 and high 92, demonstrate both support sides; a shorter ordinary pair demonstrates a quicker switch sequence. Include one visible avoidable trap and the same free instant bonus before the finish for every mode.

The safe route bypasses every marked stone from the other wall and avoids all rewards. GravityAirFlip, LinearAirFlip and GravityUnlimited use supported launches only on this ordinary comparison. JumpGravityFlip may use a second air press to change walls; this is its normal control, not an exception to hide. The ideal route intentionally lands twice, uses concrete stone support and collects the final free instant while avoiding the trap.

Prefer common geometry with different authored input arrays. If a common placement forces a narrow or impossible hybrid approach, document the actual conflicting constraints before choosing mode-specific geometry. Do not force the old TUNE positions into this model.

The final bonus must have enough track after its latest real contact to serve the whole earned pulse; a single fresh boost requires 198 world units. Separately verify all reachable earned backlog on the intentional-trap route. Do not claim an arbitrary queued history is safe merely from this single-pulse bound, and do not add an otherwise pointless second trap for a stress test.

## Unlimited passage concept

A separate short course will use real gate openings at alternating heights, with each opening accounting for both player radii. Entering one passage then reaching a differently positioned next passage must require a deliberate height correction; floating in an empty central corridor does not demonstrate this mode.

Prefer 3–4 meaningful openings and press spacing around 0.2–0.3 seconds or longer. Do not add a cooldown or change physics to accommodate browser automation. Prove an actual complete route, report the shortest repeat interval and measure timing windows. The special course is evaluated separately from the safe no-repeat comparison route.

## Required later evidence

- Exact safe/ideal/trap input-distance arrays and actual event times for each production mode.
- Both players, floor/ceiling landing, actual SupportKind/stone ID, reward counts and zero unintended collisions.
- Actual horizontal speed through every landing and free bonus; fully served pulse accounting and empty queue at finish.
- Individual actual-time perturbations, first/last/interior action gaps and smallest air-repeat interval. Nominal estimates are not proofs.
- Selected recoverable errors: retain real support, spent charge and queue; wait the actual remaining penalty plus 150 ms reaction, then demonstrate reaching the next obstacle's trailing edge without another hit. State sampled limits explicitly.
- Independent scheduler/audit and browser input evidence remain separate from this harness.
