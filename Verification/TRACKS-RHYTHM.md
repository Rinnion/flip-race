# Three-track rhythm review

18 October 2026. Read-only design review against `flip-race/docs/flip-race-three-tracks-plan.md`.

Reviewed `Assets/Scripts/Core/TracksFull.cs` SHA-256:
`D3B0285856E2D7A45284435A5AD5784850F6713DA1643B961CBBD9D1CDEF7A8D`.

**Verdict: the earlier Long/Short rhythm finding is resolved.** The source now authors three longer sequences versus six shorter groups. No further geometry change is requested by this review. Browser judgement of readability and play feel remains pending; physical route/window/recovery proofs are separate evidence.

## Measurement

A separate scratch runner used production `RaceSimulation.CreateTrial(track, FlightPresets.Chosen)`, `Press` and `Advance`. Both players followed the authored distance route. Each step was at most 1/240 second and remaining distance / 440; therefore inputs occurred at their authored X with actual boost timing. The numbers below are simulation elapsed seconds, not X differences divided by base speed. Every measured safe/full run finished without hits: 36.0 / 33.2 seconds.

One authored task counts as one maneuver, including its necessary air taps. A two-ring chain is one task, not two rewards or two taps. The exact finish target counts as its own task. Each current track has **16 maneuvers**.

## Long: 5 | 6 | 5 maneuvers

| Sequence | Authored tasks | First → last input, full route | Pause before next sequence |
|---|---|---:|---:|
| 1 | Low 300; floor pad 650; low 1050; before 1500; free ring 1950 | 0.980789 → 8.127273 | **2.563636 s** |
| 2 | High 2750; chain 3000; low 3600; ceiling pad 4000; high 4500; large pad 4800 and departure | 10.690909 → 20.672727 | **3.136364 s** |
| 3 | After 5900; middle ring 6500; low 7000; low 7300; finish target | 23.809091 → 32.946178 | Finish at 33.2 |

Inside sequences, gaps between the end of one maneuver's inputs and the start of the next are 1.225–2.268 s, excluding the deliberately separate final approach of 2.847 s. The safe route skips optional ring actions; its two sequence boundaries are 5.795 and 2.568 s. Neither route requires repeated tapping throughout a rest.

## Short: 3 | 3 | 2 | 3 | 3 | 2 maneuvers

| Group | Authored tasks | First → last input, full route | Pause before next group |
|---|---|---:|---:|
| 1 | Low 300; high 700; low 880 | 0.980789 → 3.617153 | **2.224580 s** |
| 2 | Ceiling pad 1450; free ring 1750; low 2160 | 5.841732 → 8.835334 | **2.291938 s** |
| 3 | Before 2780; chain 3150 | 11.127273 → 13.676454 | **2.318474 s** |
| 4 | Large floor pad 3850 and departure; low 4500; floor pad 5000 | 15.994928 → 20.178096 | **3.176449 s** |
| 5 | After 5800; middle ring 6350; high 7000 | 23.354545 → 28.459091 | **2.185334 s** |
| 6 | Low 7420; finish target at Y90 | 30.644425 → 32.794563 | Finish at 33.2 |

Internal maneuver-to-maneuver gaps are 1.094–2.041 s, with a separate 2.150 s final approach. Thus group boundaries now exceed the ordinary internal waits. The final boundary is only slightly longer than the final approach; it is a softer break, not evidence of an additional hidden timing margin. Safe-route group boundaries are 2.591 / 1.928 / 3.068 / 3.110 / 2.185 s: skipping rewards changes some spacing, so this report does not claim the same contrast for every route. The full route no longer shares the old Long ending.

## Flow and shared content

Flow has 16 tasks with four passages interleaved among the same reward families. Passage center bands are lower 128–222, upper 18–112, middle 74–166 and upper 18–112 world units after accounting for the hero radius. Lower and upper passages have no common safe center height. Full-route inter-maneuver waits are 1.172–2.722 s, with a 3.302 s final approach; there are no authored six-part rest boundaries. This provides a separate passage/height emphasis instead of repeating the stone-only Long/Short layout.

All three contain the required 9 ordinary positive rewards, 2 avoidable negative rings and separate finish plane. There are two standard 64×48 landing stones on opposite sides and one 200×92 large landing stone. Before/after linked rewards, two independent free rings and one two-ring air chain are distributed among ordinary obstacles. Ring heights include Y≈200 near the floor, Y120 center and Y60 at three-quarter height, with mirrored positions where appropriate.

## Visibility interpretation

The checked unit is a local maneuver that must be planned before its first committed input. The full airborne chain extends 208 world units from its first input; linked before/after combinations extend at most 308, and the large platform's physical end is approximately 201.1 units ahead of its first full-route input. Their traps/targets are inside the conservative 1080-world-unit ahead range at the minimum mobile layout.

A rhythmic group is not automatically one uninterrupted flight. Short groups 4 and 5 span more than 1080 world units end-to-end; later tasks follow independent support/decision opportunities. This review does **not** claim the entire group is visible at its first input. The mandatory target of each local airborne combination is visible in advance. Browser review must still inspect actual overlays, marker readability and the upper lane's rotation.

## Earlier finding retained for comparison

The prior source `3F655171…` had Long boundaries of 1.20 / 1.77 s and Short boundaries overlapping ordinary internal waits, plus the same final input/time sequence on both tracks. Counting those coordinates as named groups alone did not demonstrate distinct rhythms. The numbers above replace that preliminary result; the earlier layout is not the reviewed release candidate.
