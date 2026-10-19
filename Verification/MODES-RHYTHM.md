# MODES — independent rhythm review of the final two-profile tracks

19 October 2026. Read-only review of frozen `TracksFull.cs` **717C0BE9560498FFABFEF56185A235B4F76EE92D9B4373173EF3AFAFA67E1861** and `RaceSimulation.cs` **CFA0A419C9CE9FC7C0F2A7F075CACC8E7CD3C9655D12412037AA2E5D4BC7838B**. No Assets or geometry were changed for this review.

The common geometry retains its three arrangements. The primary jump-and-flight control retains the authored long-series / short-links grouping. FastFlip has different grounded actions and fewer compulsory decisions; the same input grouping is **not** claimed for both physics. The full collection route still takes 33.2 seconds for each track and either control. This distinction was reported to the parent and accepted without further geometry changes.

## Independent replay

The reviewer ran all three safe and full routes for both profiles: **12 complete games**, symmetric actual input to both players. Every run had zero hits and zero negative rewards; safe runs had zero rewards and finished in 36 seconds, full runs earned nine ordinary positives plus the finish target and finished in 33.2 seconds.

The scheduler calls production `Press` and `Advance`, advancing by at most `min(1/240 second, remaining input distance / 440)`. It does not clone states, copy the author's measured timestamps, or substitute its own trajectory. Actual input times are recorded in [modes-rhythm-independent.json](modes-rhythm-independent.json); the source is archived as [modes-rhythm-replay.cs.txt](modes-rhythm-replay.cs.txt). These are rhythm measurements, not another claim of exhaustive collision, recovery or timing-window coverage.

| Control | Track | Route | Taps | First tap | Largest interior gap | Last tap → finish |
|---|---|---|---:|---:|---:|---:|
| Primary | Long | Full | 25 | 0.981 s | 3.136 s | 0.155 s |
| Primary | Short | Full | 25 | 0.981 s | 3.176 s | 0.155 s |
| Primary | Flow | Full | 24 | 0.881 s | 3.401 s | 0.155 s |
| FastFlip | Long | Full | 18 | 0.682 s | 4.968 s | 0.200 s |
| FastFlip | Short | Full | 18 | 0.682 s | 4.082 s | 0.200 s |
| FastFlip | Flow | Full | 21 | 2.914 s | 3.032 s | 0.200 s |
| Primary | Long | Safe | 21 | 0.981 s | 5.795 s | 3.201 s |
| Primary | Short | Safe | 21 | 0.981 s | 3.886 s | 2.656 s |
| Primary | Flow | Safe | 20 | 0.881 s | 4.604 s | 3.656 s |
| FastFlip | Long | Safe | 2 | 0.682 s | 11.136 s | 24.182 s |
| FastFlip | Short | Safe | 4 | 0.682 s | 16.227 s | 2.955 s |
| FastFlip | Flow | Safe | 5 | 6.364 s | 17.504 s | 3.636 s |

Taps and authored manoeuvres are different quantities: a jump plus air reversal, a two-reward chain, or a large-platform approach and departure can use multiple taps for one authored task. The primary layout still contains 16 authored tasks including the finish on each track. The counts above are physical input edges, not a claim of 25 independent obstacles.

## Primary control: observed series and links

Long retains groups **5 | 6 | 5**. The measured gap from the last input of one group to the first of the next is **2.564 s** and **3.136 s**. Its regular within-group task transitions are approximately 1.225–2.268 seconds; the final approach is 2.946 seconds. Small 0.250–0.410 second pairs inside a manoeuvre are real air reversals or platform departure, not group separators.

Short retains **3 | 3 | 2 | 3 | 3 | 2**. Its five group-boundary gaps are **2.225, 2.292, 2.318, 3.176 and 2.185 seconds**. Regular within-group transitions reach 2.041 seconds. The last low-stone-to-finish approach is 2.401 seconds, so the final two-task group has a softer boundary than the earlier pairs/triples; it is not uniformly separated by a larger pause at every boundary.

Flow is more continuous, with regular task transitions approximately **1.144–2.722 seconds**; the final approach is 3.401 seconds. Its lower, upper and middle gate openings remain distinct. The lower opening is 114–240, the upper 0–126 and the closed middle 12–228 with width 8. In particular, a near-wall opening permits a grounded wall runner; it is not a compulsory airborne manoeuvre for every profile or route.

## FastFlip: different actions on the same geometry

The full FastFlip routes contain **18 / 18 / 21** inputs on Long / Short / Flow, all using genuine support. They traverse the same stones and rewards, with true landings between chain inputs. They do not reproduce the primary control's input grouping:

- Long has a 3.255-second chain-to-pad interval inside the second authored section, 4.364 seconds from the large-platform departure to the next after-ring action, and 4.968 seconds from the last middle-ring action to the finish setup. Its final section is substantially quieter.
- Short's chain-to-next-platform setup interval is only 1.219 seconds, while its large-platform departure to the next narrow landing is 4.082 seconds. Consequently, describing FastFlip as the same six short input groups would be misleading.
- Flow's full route is the most even FastFlip sequence: its largest interior interval is 3.032 seconds; the other intervals range down to the 0.442-second supported chain pair. Setup flips for collecting optional rewards add purposeful grounded actions without introducing hidden air repeats.

The reward-free FastFlip bypasses are intentionally very sparse in this common geometry. Long can complete with two inputs and then run 24.182 seconds without another input; Short and Flow also have 16–17.5-second interior gaps. These are real safe routes, but their duration alone must not be used as evidence of dense gameplay. No claim of equal mandatory action density between the controls is made.

## Final geometry and historical evidence

The final finish targets are **Y32 on Long/Short and Y208 on Flow**, full target height 16, plane X7876. FastFlip adds a genuine wall setup at X7700 (Long/Flow) or X7720 (Short), then the final input at **7853.945775241146**. This review used those final values, not the earlier Flow Y180 target.

`Tools/TwoModesFlowSearch` and its original `Artifacts/flow-routes.json` are preserved historical search evidence for the earlier **Flow Y180** checkpoint. Their 5,220-check result and approximately ±8.4 ms final window do **not** certify the final Y208 runtime. Final production routes are in [modes-routes.json](modes-routes.json); the production author's broader timing/physics tests are separate evidence. No historical result was relabelled as a final pass.

The completed UI integration review found no actionable issues in Awake migration ordering, preset/track preservation of custom data, ready/card hitboxes or lazy-frame cache lifetime. Pixel and codec tests were not repeated. Browser and physical-phone behaviour are outside this code/rhythm review.
