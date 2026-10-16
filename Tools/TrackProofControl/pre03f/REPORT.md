# CONTROL-03: frozen geometry and route proof

Date: 2026-10-17. Runtime source: `Assets/Scripts/Core/TracksControl.cs`.

SHA256: `05D1FC9375377CFAC96A568338B89FE33D8397D7C21626D5C1920DBF619488E6`.

Full local proof: **17,985 checks PASS**, reproduced by root before the final Unity build. The independent delivery audit reports **16,098 assertions, 226 complete games and four recovery replays PASS** on the same track source. Browser validation of the finished obstacle courses is root's separate CONTROL-04 gate; this report does not claim it has already occurred.

## Shared comparison

Length 4,620; ordinary travel takes 21 seconds. The first stone starts at 1,000, with its expanded contact beginning at 986 (4.4818 seconds without a reward). Every mode uses exactly the same obstacle and reward geometry.

| Stone | Side | X | Width | Height | Landing marker |
| --- | --- | ---: | ---: | ---: | --- |
| control-floor-pad | floor | 1000 | 200 | 48 | center 1060, width 52 |
| control-ceiling-pad | ceiling | 1940 | 200 | 92 | center 2000, width 52 |
| control-quick-floor | floor | 2600 | 64 | 48 | none |
| control-quick-ceiling | ceiling | 2940 | 64 | 92 | none |
| control-quick-exit | floor | 3260 | 64 | 48 | none |

The visible negative is at `(3110,110)`, radius 8. The preserved free instant before the finish is at `(4310,110)`, radius 9. There are three positive rewards: two landings and the final instant.

| Mode | Profile | Safe finish | Ideal finish | Intentional trap finish |
| --- | --- | ---: | ---: | ---: |
| GravityAirFlip | g1000, impulse70 | 21.0 | 20.1 | 20.4 |
| LinearAirFlip | transverse speed265 | 21.0 | 20.1 | 20.4 |
| JumpGravityFlip | g662.5, jump400 | 21.0 | 20.1 | 20.4 |
| GravityUnlimited | g1000, impulse70 | 21.0 | 20.1 | 20.4 |

All nominal routes have zero collisions. Safe routes have no rewards and no air repeats except the normal second tap of JumpGravityFlip. Ideal routes earn all three positives and no negative. `NegativeRoute(mode)` earns the same three positives plus the negative. Every route ends with all earned pulses completed, no active effect and an empty queue.

Ideal landings occur near the marker centers at actual times 4.818181818 and 8.790909090. Event-time replay confirms the physical plane, explicit `Stone` support with the correct ID/side, restored air charge and immediate acceleration. These are stable landings, not merely inertial rewards. From either center, boosted running to the edge lasts 0.424242 seconds. Separate branches run for 0.3 seconds, then depart from either stone in every mode; another branch walks off under the boost without a new collision. The played ideal route departs from the first stone after 0.303030 seconds and walks off the second.

The first Gravity/Unlimited ideal air return reverses at 0.52 seconds. It touches the opposite wall by inertia without creating support, then lands stably on the floor marker. This gives a wider input window than the earlier short arc without changing physics or marker width. Linear reverses after 0.42 seconds. Jump reaches the first platform with its ordinary jump and uses a second tap after departing that platform.

## Actual input windows and gaps

These are one-input-at-a-time perturbations of the actual race clock. Later actions remain distance-triggered. A 5 ms grid finds the contiguous successful range around the nominal route. No claim is made that every combination of timing errors succeeds. The outcome includes intended rewards, no unintended reward/negative/collision and complete effects before the finish.

| Mode | Safe: minimum early/late | Ideal: minimum early/late |
| --- | --- | --- |
| GravityAirFlip | at least 150/150 ms | 60/70 ms |
| LinearAirFlip | at least 150/150 ms | 55/55 ms |
| JumpGravityFlip | at least 150/150 ms | 115/115 ms |
| GravityUnlimited | at least 150/150 ms | 60/70 ms |

The safe search is capped at 150 ms. Every comparison input separately preserves its full outcome at ±40 ms.

| Ideal route | First tap | Smallest gap | Largest interior gap | Last tap to finish | Air inputs |
| --- | ---: | ---: | ---: | ---: | ---: |
| GravityAirFlip | 3.652480 | 0.520000 | 4.989909 | 1.482818 | 1 |
| LinearAirFlip | 4.159314 | 0.420000 | 5.001372 | 1.471355 | 1 |
| JumpGravityFlip | 3.745754 | 0.242424 | 4.783227 | 1.439500 | 3 |
| GravityUnlimited | 3.652480 | 0.520000 | 4.989909 | 1.482818 | 1 |

The final instant's measured contact-to-finish interval is at least 1.157645 seconds. Its latest geometric circle contact is at 4333, leaving 287 units, more than the 198 travelled during one complete boost. Actual route proofs also verify the intentional-negative history rather than relying only on that bound. Independent final-only duels finish 20.1 versus 20.4 with zero hits: the final bonus gives its advantage through real movement.

## Separate unlimited passages

Length 2,860, finish 13.0 seconds. Four gates, each width 20, start at 900, 1200, 1500, 1800. Their open intervals alternate `[4,126]` and `[114,236]`. With radius 14, legal center bands are `[18,112]` and `[128,222]`; no shared middle height can pass all gates.

The actual route passes upper gates with center Y 64.049587–75.950413 and lower gates with center Y 164.049587–175.950413. Each series consists of one supported launch and two accepted air repeats before landing. A real wall landing between series resets accumulated trajectory error. There are eight air repeats, with the smallest press interval 0.328058 seconds.

```text
750.254574710043, 822.427287355021, 910,
1050.254574710043, 1122.427287355022, 1210,
1350.254574710043, 1422.427287355022, 1510,
1650.254574710043, 1722.427287355022, 1810
```

The route passes with zero hits at all six simulation step limits. Every input individually tolerates ±45 ms on the 5 ms grid; the independent audit separately confirms ±45 ms. First tap: 3.410248 seconds; largest interior gap: 0.637521; last tap to finish: 4.772727. This focused passage demo has no rewards or trap; the shared comparison supplies the landing/reward test for the unlimited mode.

## Recovery scope

The comparison proof samples actual input-time offsets of ±80, ±160, ±240, ±400 and ±640 ms, retaining the first actual collision and real support/charge/effect state. It waits the true remaining recovery plus 150 ms reaction, then searches stay, one accepted press or two accepted presses. Success requires reaching the next obstacle's trailing edge plus radius plus one unit with no extra collision.

138 captured collision states passed: Gravity safe/ideal 7/27, Linear 6/27, Jump 15/24, Unlimited 7/25. This is a finite set of errors, not all possible histories. A hit on the last obstacle has no next obstacle, so that case is explicitly vacuous. Four independently replayed witnesses include wrong landings with a spent charge and delayed Jump inputs in the quick sequence. This recovery search covers the shared comparison, not the separate passage course.

## Artifacts

- [last-proof.txt](last-proof.txt): final complete route proof output.
- [routes.json](routes.json): exact distance arrays, actual input times/support, reward times and finish metrics for every safe/ideal/trap route and the passage route.
- [README.md](README.md): commands and API.
- [Independent harness](../../Verification/control-geometry-independent.cs.txt): separate conservative scheduler, mixed races, landing replays and passage extrema.

The scheduler uses production `Press`/`Advance` and actual speed. Large steps, effect transitions, support and snapshots are explicit. Diagnostic snapshot publication intervals are not presented as browser rendering FPS.
