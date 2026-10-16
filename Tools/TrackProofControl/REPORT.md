# CONTROL-03F: frozen geometry and route proof

The original CONTROL-03 geometry and results are preserved in [pre03f](pre03f/STATUS.md). This report covers its replacement finish-plane reward; both landing platforms, the ordinary obstacles, trap and unlimited passage course remain unchanged.

Date: 2026-10-17. Runtime source: `Assets/Scripts/Core/TracksControl.cs`.

SHA256: `588BE383B503F40FFC8083450237296AD1A6EA0954488684712FD5A3F74449D4`.

Full local proof: **20,451 checks PASS**. Independent CONTROL-03F auditing and browser validation are separate gates; the old independent/browser results are not presented as evidence for this replacement reward.

## Shared comparison

Length 4,620; ordinary travel takes 21 seconds. The first stone starts at 1,000, with its expanded contact beginning at 986 (4.4818 seconds without a reward). Every mode uses exactly the same obstacle and reward geometry.

| Stone | Side | X | Width | Height | Landing marker |
| --- | --- | ---: | ---: | ---: | --- |
| control-floor-pad | floor | 1000 | 200 | 48 | center 1060, width 52 |
| control-ceiling-pad | ceiling | 1940 | 200 | 92 | center 2000, width 52 |
| control-quick-floor | floor | 2600 | 64 | 48 | none |
| control-quick-ceiling | ceiling | 2940 | 64 | 92 | none |
| control-quick-exit | floor | 3260 | 64 | 48 | none |

The visible negative is at `(3110,110)`, radius 8. The finish reward is a center-X plane at 4576 with center-Y range `[52,68]`, 44 units before the tape. It has no circular radius expansion. There are three positive rewards: two ordinary landing boosts and one separately accounted finish boost.

| Mode | Profile | Safe finish | Ideal finish | Intentional trap finish |
| --- | --- | ---: | ---: | ---: |
| GravityAirFlip | g1000, impulse70 | 21.0 | 20.3 | 20.6 |
| LinearAirFlip | transverse speed265 | 21.0 | 20.3 | 20.6 |
| JumpGravityFlip | g662.5, jump400 | 21.0 | 20.3 | 20.6 |
| GravityUnlimited | g1000, impulse70 | 21.0 | 20.3 | 20.6 |

All nominal routes have zero collisions. Safe routes have no rewards and no air repeats except the normal second tap of JumpGravityFlip. Ideal routes earn all three positives and no negative. `NegativeRoute(mode)` earns the same three positives plus the negative. Every route ends with all ordinary pulses completed, no active effect, an empty queue and no active finish boost. The finish boost records 0.1 moving seconds and 22 units of extra distance; it does not increment ordinary boost counters.

Ideal landings occur near the marker centers at actual times 4.818181818 and 8.790909090. Event-time replay confirms the physical plane, explicit `Stone` support with the correct ID/side, restored air charge and immediate acceleration. These are stable landings, not merely inertial rewards. From either center, boosted running to the edge lasts 0.424242 seconds. Separate branches run for 0.3 seconds, then depart from either stone in every mode; another branch walks off under the boost without a new collision. The played ideal route departs from the first stone after 0.303030 seconds and walks off the second.

The first Gravity/Unlimited ideal air return reverses at 0.52 seconds. It touches the opposite wall by inertia without creating support, then lands stably on the floor marker. This gives a wider input window than the earlier short arc without changing physics or marker width. Linear reverses after 0.42 seconds. Jump reaches the first platform with its ordinary jump and uses a second tap after departing that platform.

## Actual input windows and gaps

These are one-input-at-a-time perturbations of the actual race clock. Later actions remain distance-triggered. A 5 ms grid finds the contiguous successful range around ordinary inputs; the precise final input uses a 1 ms grid. No claim is made that every combination of timing errors succeeds. The outcome includes intended rewards, no unintended reward/negative/collision and complete effects before the finish.

| Mode | Safe: minimum early/late | Ideal before final input | Final input early/late |
| --- | --- | --- | --- |
| GravityAirFlip | at least 150/150 ms | 60/70 ms | 25/25 ms |
| LinearAirFlip | at least 150/150 ms | 55/55 ms | 30/30 ms |
| JumpGravityFlip | at least 150/150 ms | 115/115 ms | 25/25 ms |
| GravityUnlimited | at least 150/150 ms | 60/70 ms | 25/25 ms |

The safe search is capped at 150 ms. Every comparison input before the deliberately difficult final maneuver separately preserves its full outcome at ±40 ms. The final input is centered in the time window, rather than rounded to a convenient distance; its analytic half-windows are approximately 25.788, 30.189 and 25.456 ms for Gravity, Linear and Jump respectively.

| Ideal route | First tap | Smallest gap | Largest interior gap | Last tap to finish | Air inputs |
| --- | ---: | ---: | ---: | ---: | ---: |
| GravityAirFlip | 3.652480 | 0.520000 | 6.332510 | 0.340218 | 1 |
| LinearAirFlip | 4.159314 | 0.420000 | 6.399142 | 0.273585 | 1 |
| JumpGravityFlip | 3.745754 | 0.242424 | 6.193323 | 0.229404 | 3 |
| GravityUnlimited | 3.652480 | 0.520000 | 6.332510 | 0.340218 | 1 |

## Precise finish maneuver

Only the last input changes from the original comparison routes:

| Mode | Last input distance |
| --- | ---: |
| GravityAirFlip / GravityUnlimited | 4523.152127406574 |
| LinearAirFlip | 4537.811320754717 |
| JumpGravityFlip | 4547.531146724642 |

The player departs the ceiling once and crosses the center-X plane with center Y in `[52,68]`. Event-time replay checks no reward one microsecond before contact, the exact X at contact and a clean speed-220 approach. Contact activates speed 440; 0.05 seconds later the player has moved 22 units and has not finished. The next 0.05 seconds reaches the tape. Thus actual final-only duels finish 20.3 versus 20.4, with zero hits, at all six step limits and with either player taking the bonus. The real crossing time selects the winner. Vertical motion is not replaced by a teleport or an artificial time deduction.

The last possible ordinary contact is the negative's trailing expanded edge at `3110 + 8 + 14 = 3132`. The remaining 1444 units to the finish plane require at least `1444 / 330 = 4.375758` moving seconds. There are only two once-only 0.6-second landing pulses and one once-only 0.6-second negative, totaling at most 1.8 seconds of service even if all remain queued. The proof enumerates all eight subsets. Recovery freezes distance and effect service together, so it cannot use up this spatial clearance while retaining a backlog. Every attainable subset therefore reaches the plane with all ordinary effects naturally complete; no effect is cleared artificially.

## Separate unlimited passages

Length 2,860, finish 13.0 seconds. Four gates, each width 20, start at 900, 1200, 1500, 1800. Their open intervals alternate `[4,126]` and `[114,236]`. With radius 14, legal center bands are `[18,112]` and `[128,222]`; no shared middle height can pass all gates.

The actual route passes upper gates with center Y 64.049587–75.950413 and lower gates with center Y 164.049587–175.950413. Each series consists of one supported launch and two accepted air repeats before landing. A real wall landing between series resets accumulated trajectory error. There are eight air repeats, with the smallest press interval 0.328058 seconds.

```text
750.254574710043, 822.427287355021, 910,
1050.254574710043, 1122.427287355022, 1210,
1350.254574710043, 1422.427287355022, 1510,
1650.254574710043, 1722.427287355022, 1810
```

The route passes with zero hits at all six simulation step limits. Every input individually tolerates ±45 ms on the 5 ms grid. First tap: 3.410248 seconds; largest interior gap: 0.637521; last tap to finish: 4.772727. This focused passage demo has no rewards or trap; the shared comparison supplies the landing/reward test for the unlimited mode.

## Recovery scope

The comparison proof samples actual input-time offsets of ±80, ±160, ±240, ±400 and ±640 ms, retaining the first actual collision and real support/charge/effect state. It waits the true remaining recovery plus 150 ms reaction, then searches stay, one accepted press or two accepted presses. Success requires reaching the next obstacle's trailing edge plus radius plus one unit with no extra collision.

138 captured collision states passed again after the finish change: Gravity safe/ideal 7/27, Linear 6/27, Jump 15/24, Unlimited 7/25. This is a finite set of errors, not all possible histories. A hit on the last obstacle has no next obstacle, so that case is explicitly vacuous. The earlier independent audit replayed four witnesses on the unchanged obstacle geometry, including wrong landings with a spent charge and delayed Jump inputs in the quick sequence. This recovery search covers the shared comparison, not the separate passage course.

## Artifacts

- [last-proof.txt](last-proof.txt): final complete route proof output.
- [routes.json](routes.json): exact distance arrays, actual input times/support, reward times and finish metrics for every safe/ideal/trap route and the passage route.
- [README.md](README.md): commands and API.
- [Historical independent harness](../../Verification/control-geometry-independent.cs.txt): pre-CONTROL-03F conservative scheduler, mixed races, landing replays and passage extrema. Its old final-reward expectations do not prove the replacement finish plane.

The scheduler uses production `Press`/`Advance` and actual speed. Large steps, effect transitions, support and snapshots are explicit. Diagnostic snapshot publication intervals are not presented as browser rendering FPS.
