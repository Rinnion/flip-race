# MIX-02 local straight-gate proposal

21 October 2026. Tool-only geometry; production Assets are unchanged by this proof.

`dotnet run --project Tools/MixedStageProof -c Release -- --straight` passes **3,270 checks / 176 games**. Captures: `mixed-straight-proof.txt`, `mixed-straight-proof.json`.

Both physical bands share X800 and width8, with `GapTop=48`, `GapBottom=228`, both offsets0. Thus the upper band is48 units and lower band12. Mirror uses `GapTop=12`, `GapBottom=192`. This is an asymmetric broad straight window, not a narrow central-height window.

| Profile | Service speed | Inputs X | Semantics | Individual timing checks |
|---|---:|---|---|---|
| Main | 110 | 745.8867924528302 | One natural jump; original wall | ±60 ms |
| Main | 220 | 687.7735849056604 | Same | ±60 ms |
| Main | 330 | 629.6603773584906 | Same | ±60 ms |
| Fast | 110 | 762.9740740740741, 782.7740740740741 | Wall departure, One in air, original wall | ±25 ms service only |
| Fast | 220 | 752.3, 788.6 | Same; repeat after .165 s | ±60 ms |
| Fast | 330 | 729.75, 780.9 | Same; repeat after .155 s | ±60 ms |

To move the gate, translate all X values equally. Main's centred formula is `gateX + 4 - speed*350/662.5`. Mirror is reached by real supported setup, not player-state assignment. Every nominal row passes both orientations at30/60/144 Hz and frame sizes .317,2.5,100 seconds, clears both bands, and restores the actual original-wall support/charge. Constant-speed rows are labelled held-speed service fixtures, not earned effects.

Two additional real-ring fixtures retain exactly this gate. An instant ring at X634/Y226 is collected at612 and ends at810, inside the passage. A negative at760/Y226 is collected at738 and ends at804, also inside. They use the corresponding330/110 input arrays above; no effect or player state is injected. Both profiles and mirrors pass all six frame sizes. Speed is330/110 after the action, returns to220 by the rear, and the actual finish equals1600/220 minus/plus .3 seconds. The JSON includes actual times, support, velocity and observed speeds.

Exploratory 2 ms scans found Fast220 individual ranges first−78/+70 ms, second−70/at least+138 ms; Fast330 first−98/+84 ms, second−70/at least+138 ms. Those are sampled one-input ranges, not exact continuous roots or a joint timing box. The explicit ±60 endpoints are the acceptance check. The narrower110 Fast service is reachable but is not certified as a ±60 mandatory path.

The returned Fast wall differs from a single monotone wall-to-wall flip. Full-course composition must account for that support and add a real later departure where necessary; it must not assume an opposite-wall exit from the older one-tap candidate.
