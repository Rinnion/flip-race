# TUNE authored trial proof

Run from the Unity project with .NET 10:

```powershell
dotnet run --project Tools/TrackProofTune/TrackProofTune.csproj
```

Final recorded result: **PASS 47 929 checks**. Full output is in [last-proof.txt](last-proof.txt). The harness compiles production Core sources and calls production `Press` / `Advance`; it does not approximate vertical motion or replace collision detection. Diagnostic window searches deliberately exclude their expected failing probes from the PASS count.

The frozen `TracksTune.cs` SHA-256 is `A4A0F5404DA20CE3D5348E33C28A0B9A80060DE5B7F1593E8B7E798DD989BC4F`. `TracksV4.cs` and its old challenge remain unchanged.

After the verified build, surplus empty lines at EOF were removed without changing code. The committed `TracksTune.cs` SHA-256 is `86932F9E38CCEABACB74DA14D1CB44DC5CB5D139A385B53BD8E921FECED38628`; the table above identifies the exact source used for the build and route audit.

## Runtime API and geometry

`TracksTune.ChallengeId` is `v4-tune`. `Challenge(MotionProfile profile = null)` returns the new trial; `Get(id, profile = null)` delegates old IDs to `TracksV4.Get`. `NegativeRoute(profile)` exposes the intentional trap alternative. `Track.SafeRoute` and `RiskRoute` belong to the selected profile.

All four profiles use **identical geometry**, 4620 world units / 21 seconds at base speed. Only their authored input coordinates differ. First possible stone contact is x886, after 4.027 seconds. Eleven stones alternate or repeat floor/ceiling sides and use heights 48 and 92. The low instant is over a floor stone; the high instant is on the free side of a ceiling stone.

| Item | X | Y | Radius | Behavior |
|---|---:|---:|---:|---|
| Low instant | 1212 | 116 | 22 | Immediate bonus while bypassing the low floor stone |
| Free return ring | 1440 | 220 | 8 | Ordinary free reward after the low return |
| Linked before | 1770 | 204 | 6 | Awaits clearance of the floor stone at 1820 |
| Visible negative trap | 2945 | 170 | 6 | Catches a hasty return |
| Linked after | 3060 | 51 | 6 | Awaits completed return and clearance after the ceiling stone at 2820 |
| High instant | 3432 | 164 | 22 | Immediate bonus while bypassing the high ceiling stone |
| Final instant | 4310 | 110 | 9 | Optional final departure gives a real earlier finish |

This is a separate short trial, not a replacement of the original V4 layout or full V3 tracks. Compared with the initial draft, the two overhead instant radii were increased from 9 to 22 after actual ±40 ms perturbations exposed missed bonuses and premature returns onto stone edges. The floor stone preceding the high instant was moved from x3230 to x3170 after a sampled late departure exposed a forced next collision. These are shared changes for all profiles.

## Routes and outcomes

Inputs are **horizontal world-distance coordinates**, not elapsed times. The scheduler uses actual speed and splits at effects; when a new reward starts inside an outer step, it solves the exact input time on copied production state. Copies include the immutable motion profile, instant metadata, all pulse counters, partial effect time, and the pending queue.

| Profile | Gravity | Departure impulse | Safe | Ideal | Trap alternative |
|---|---:|---:|---:|---:|---:|
| base | 662.5 | 0 | 21.0 s | 19.2 s | 19.8 s |
| gravity | 1000 | 0 | 21.0 s | 19.2 s | 19.8 s |
| impulse | 662.5 | 140 | 21.0 s | 19.2 s | 19.8 s |
| combined | 1000 | 140 | 21.0 s | 19.2 s | 19.8 s |

Safe has no collisions, rewards, or airborne repeats. Ideal has no collisions, all six positives including all three instants, no negative, and two purposeful airborne repeats. The trap alternative has no collisions, five positives including all three instants, and one negative; it misses the linked-after reward. Every earned effect completes before finish, with no active effect or pending queue. Symmetric routes give exact ties.

```text
SAFE
base:     [1020,1300,1640,2000,2350,2650,3010,3220,3920]
gravity:  [1020,1350,1640,2000,2350,2650,3010,3220,3920]
impulse:  [1020,1350,1640,2000,2350,2650,3010,3220,3920]
combined: [1020,1370,1640,2000,2350,2650,3010,3220,3920]

IDEAL
base:     [1025.2,1114.9,1713.3,1980,2350,2650,2900.1,3183.2,3319.8,3710,3920,4191.6]
gravity:  [1058.6,1131.5,1723.9,1980,2350,2650,2929.8,3241.6,3339.3,3710,3920,4213.6]
impulse:  [1060,1109,1743.2,1980,2350,2650,2940,3237.8,3314.8,3710,3920,4229.3]
combined: [1083,1128.4,1745.3,1980,2350,2650,2957.1,3279.4,3336.6,3710,3920,4239.6]

TRAP ALTERNATIVE
Clone ideal; subtract 55 from zero-based input[6].
Set input[7] to base=3215.1, gravity=3254.1, impulse=3251.5; combined remains unchanged.
```

## Actual timing and input windows

Times exclude the countdown and are measured using the production simulation, including every boost.

| Profile | Low air repeat | High air repeat | First input | Maximum interior gap | Last input to finish | Final pickup to finish |
|---|---:|---:|---:|---:|---:|---:|
| base | 0.407727 s | 0.475758 s | 4.660000 s | 2.120000 s | 1.647273 s | 1.166041231 s |
| gravity | 0.331364 s | 0.387424 s | 4.811818 s | 2.092727 s | 1.547273 s | 1.158186791 s |
| impulse | 0.222727 s | 0.287576 s | 4.818182 s | 2.282727 s | 1.475909 s | 1.162889502 s |
| combined | 0.206364 s | 0.260000 s | 4.922727 s | 2.204091 s | 1.429091 s | 1.156146644 s |

Safe and ideal routes pass at 30/60/144 Hz and outer steps 0.317 / 2.5 seconds. Every individual input passes ±40 ms of **actual elapsed time**, while other inputs retain their distance triggers. This does not guarantee arbitrary simultaneous timing errors.

The diagnostic search advances one input in 5 ms increments until the first changed outcome, capped at 120 ms. These are sampled limits for retaining the entire expected outcome, including ring counts and accepted surface-only safe inputs; a changed safe outcome may mean an extra optional reward rather than a collision.

| Profile | Safe earlier / later | Ideal earlier / later | Limiting ideal input indexes |
|---|---:|---:|---|
| base | 40 / 105 ms | 50 / 40 ms | 7 / 2 |
| gravity | at least 120 / 120 ms | 40 / 45 ms | 7 / 7 |
| impulse | 85 / at least 120 ms | 50 / 50 ms | 7 / 7 |
| combined | 90 / at least 120 ms | 40 / 45 ms | 7 / 7 |

Thus a browser automation frame around 62 ms can exceed a narrow ideal window. These limits are reported rather than treated as evidence that all coarse automated inputs must succeed. Human readability, preference, and repeated manual play remain separate checks.

## Final accelerator and effect tails

The final pickup is after the last stone. Even the latest geometric contact at x4333 leaves 287 units, exceeding the 198 units needed for its full 0.6-second boost. Actual clean pickup-to-finish times are 1.156–1.166 seconds. A mixed race that shares every preceding input and omits only the final pickup for one player finishes at 19.2 versus 19.5 seconds, with the pickup player winning through actual movement.

A separate **synthetic 880-unit fixture**, not another runtime trap, places one negative, a queued ordinary free reward, and two instants. It verifies immediate interruption of the active slow, retention of its partial remainder ahead of the old queue, extension of the active positive, preserved pulse accounting in snapshots, and complete service before the real 3.4-second finish. A finite 310-unit authored tail is not claimed to drain an arbitrary unbounded queue.

## Recovery scope

The recovery sweep shifts one authored input by multiples of 17.6 world units in [−176,+176], keeping input order. These are distance perturbations, not fixed time perturbations under effects. It captures the first collision with its true motion, charge, active effect, pending queue, and partial counters. After the remaining recovery penalty, the search waits another 150 ms before any corrective action.

| Profile | Safe collision states | Ideal collision states |
|---|---:|---:|
| base | 75 | 95 |
| gravity | 69 | 80 |
| impulse | 61 | 73 |
| combined | 63 | 77 |

All **593 sampled states** have an accepted-control continuation reaching beyond the next stone without another collision. Search considers staying, one press delayed by 0–2.5 seconds, or a second accepted press 0.15–1.8 seconds later. Actual landing and available air charge govern whether a press is accepted. A hit on the final stone has no following stone to test. This is evidence for the sampled mistakes, not an exhaustive proof for every possible input history.

The harness also removes each ideal air repeat separately and verifies that the corresponding route loses an instant or collides, so the two repeats serve the authored maneuvers. Physics, Unity presentation, builds, public publishing, and full-race track migration are outside this harness's scope.
