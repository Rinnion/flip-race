# RHYTHM — offline module catalogue

19 October 2026. Production physics and Assets remained frozen. This authoring work supplies geometry and actual input witnesses to the separate course builder; it does not assert that concatenation automatically produces a playable whole course.

The original search could not find a full Main chain and exposed narrow after/chain windows. The revised chain has two compact112 gates at X100 and420, with instant rings at X134 and454, both Y120/radius8. The first gate leads floor→ceiling and the second is its mirror. This gives the safe route two physical height changes. The full route takes the same two tasks and collects both independent impulses. The revised after module has a floor stone X160/width64/height92 and a linked after ring X340/Y120/radius8, ClearX239. It first clears the stone, then returns toward the other wall to collect or avoid that ring.

## Exports and boundary contracts

[Full-length catalogue](rhythm-modules.json): **63 560 assertions / 5 264 actual runs PASS**.

[Compact catalogue](rhythm-modules-compact.json): **63 362 assertions / 5 250 actual runs PASS**; [captured stdout](rhythm-modules-compact-proof.txt).

Both contain all actual obstacle/zone fields, four routes per module, distance inputs, actual input times, state samples and local timing windows. They enter on the floor at base speed220 with no active or queued effect. They end on the listed real wall with every earned ordinary pulse completed. Mirroring swaps floor/ceiling, transforms Y to240−Y, swaps gate offsets and maps gaps to240−GapBottom /240−GapTop.

| Kind | Original endX | Compact endX | Exit wall | Latest conservative all-work endX | Compact margin |
| --- | --- | --- | --- | --- | --- |
| gate |300|270|ceiling|0|—|
| free |380|350|ceiling|334|16|
| free-high |380|355|ceiling|350|5|
| before |500|480|ceiling|457.0000001|22.9999999|
| after |600|580|floor|560|20|
| chain |700|690|floor|674|16|
| pad |520|460|floor|446|14|
| large |700|600|floor|524|76|

Compact endX values are the shorter tested selections, not mathematical minima. The work bound sorts latest possible release positions and accumulates198 world units per ordinary reward: `end=max(end,release)+198`. Linked release includes the farther physical union edge, radius and epsilon. These modules have no negative rewards yet. Recovery freezes both distance and effects and therefore cannot invalidate this work bound; it can still change the wall/trajectory needed by the next module.

Every route passes both mirrors and dt1/30,1/60,1/144,0.317,2.5,100. Each safe input is shifted individually by ±60 ms; each full input by ±25 ms. Input times are actual elapsed times, while later unshifted inputs remain distance-triggered. Fast inputs are checked for real support. The ceiling setup is reached through real Press/Advance. No dynamic player state is injected.

## Compact route timing results

Table entries are action count / smallest symmetric local window. A150 ms result is the finite search cap; these are individual input windows, not joint jitter boxes.

| Kind | Fast safe | Main safe | Fast full | Main full |
| --- | --- | --- | --- | --- |
| gate |1 /145 ms|2 /150 ms|1 /145 ms|2 /150 ms|
| free |1 /90 ms|2 /115 ms|1 /60 ms|2 /125 ms|
| free-high |1 /80 ms|2 /125 ms|1 /60 ms|2 /50 ms|
| before |1 /150 ms|2 /150 ms|1 /100 ms|2 /100 ms|
| after |2 /150 ms|4 /150 ms|2 /100 ms|4 /135 ms|
| chain |2 /90 ms|4 /115 ms|2 /60 ms|4 /85 ms|
| pad |2 /150 ms|1 /150 ms|2 /70 ms|1 /70 ms|
| large |2 /150 ms|4 /120 ms|2 /115 ms|3 /25 ms|

The large Main full route remains the narrowest at ±25 ms. The composition must retain that tested context or replay a revised route rather than silently treating it as a wider window.

All inputs are accepted. Removing each input prevents some part of the module contract: clean passage, desired reward, stable exit or chosen exit wall. The export includes the actual omission result to make the distinction visible. In particular, some safe-after returns merely establish the exit wall; this is not evidence that they are independent mandatory obstacles. The full-course builder must remove each input again and justify it by a hit or lost optional reward in the actual continuation; otherwise it should remove the redundant setup from route and statistics.

## Independent chain collection

The chain additionally passes all four reward subsets: neither, first only, second only and both. Full and safe halves are mixed without changing geometry or simulating a pickup. Exact successful IDs, complete effect ledgers and stable common exits are checked on both mirrors, two profiles and all six dt values: **1 872 assertions /96 actual runs PASS**, [stdout](rhythm-module-chain-subsets.txt).

## Reproduction and remaining work

Commands and schema are in [Tools/RhythmCompose/README.md](../Tools/RhythmCompose/README.md). The original search output was retained; the revised candidates and both catalogues are separate files. Each runtime copy needs unique IDs for its obstacles and rewards, with corresponding `ObstacleId` remapping. A complete course still needs actual end-to-end replay, useful-input tests in context, collision recovery, negative/trap paths and finish-plane validation. No Unity build or browser pass is claimed by this CPU catalogue.

## Subsequent before-module density revision

The first before module left a long interval after its one Fast press. The new [before-revised catalogue](rhythm-modules-before.json) replaces only that module; the prior JSON files and table above remain historical evidence. The same stone X180/width64/height92 and linked ring X80/Y120 are followed by a mandatory mirrored compact112 gate: lower first atX330, upper atX442, width16, GapTop110/GapBottom130. EndX is520 and the exit is now the floor. The all-late reward work ends at457.0000001, safely before that boundary.

| Route | Inputs | Smallest measured individual window |
| --- | --- | --- |
| Fast safe |78,335|145 ms|
| Main safe |58,111,286.8,353.8|150 ms search cap|
| Fast full |22,305.5|95 ms|
| Main full |22.5,45.5,258.8,334.8|100 ms|

The next gate occupies time that previously served only as the reward tail. The complete catalogue with this one replacement passes **71 471 assertions /5 726 actual runs**, mirrors, six steps, safe ±60 ms/full ±25 ms, actual supported Fast inputs and empty completed ledgers at each exit. Evidence: [stdout](rhythm-modules-before-proof.txt), [refined candidates](rhythm-module-refined-before-candidates.json). The course builder must account for the changed exit wall and remove a redundant neighboring bare gate where appropriate; the module proof does not perform that composition itself.

## Recovery correction: before gate400

The before330 nominal proof does not imply recovery safety. The independent audit later produced a real forced second hit: after a mirrored ceiling-stone hit at localX257.001444, remaining penalty and150 ms reaction, Fast is on the ceiling at localX290.001444. Only25.998556 units remain to the gate's expanded front316, giving118.175 ms, while descending110 requires about272.785 ms. The historical witness is retained in `rhythm-course-r2-before-recovery.txt`.

The revised candidate moves only that exit gate toX400 and ends the module at590. Its stone/ring remain unchanged; the real boost ends within the approach, so full routes were recentered with production events. [New catalogue](rhythm-modules-before400.json), [stdout](rhythm-modules-before400-proof.txt): **71 493 assertions /5 731 runs PASS**, the same mirror/six-step/window/empty-exit scope. This catalogue also uses the separately proved large580 endpoint. Its before routes are:

| Route | Inputs | Smallest measured individual window |
| --- | --- | --- |
| Fast safe |78,405|145 ms|
| Main safe |60.5,111,356.8,423.8|150 ms search cap|
| Fast full |22,386|100 ms|
| Main full |22.5,45.5,331.8,412.3|100 ms|

The independent actual-error recovery check then passed **6 892 assertions /688 games /136 first-stone-hit states**, both mirrors and profiles, with old330 and new400 input schedules and shifts −0.96…+0.96 s. The exact former forced-hit state clears the entire following gate after a legal settling input, actual wall contact and a new gate departure; no finite-search escapes remained unfound. See [independent recovery](rhythm-before400-independent-recovery.txt). This is a sampled module recovery proof, not a universal full-course claim.

The optional isolated bare-gate end250 candidate also passed endpoint/window checks but was **not selected**: the composer retained end270 and instead removed one bare module in Dense. No recovery certificate is claimed for end250.
# R6 module recovery update

The latest explicit export is [rhythm-modules-r6.json](rhythm-modules-r6.json), produced with `--compact --before400 --large580 --r6-shifts`. [Output](rhythm-modules-r6-proof.txt): **71,493 assertions /5,731 actual runs**. This adds only the approved recovery spacing changes: before exit gate X420/end610; chain second gate X450 and second instant ring X484/end720. The corresponding last action(s) move by20 or30. All four routes retain the required individual timing windows, both mirrors/six dt, real wall support and empty completed effects at the module boundary. The separate full-course R6 composition/recovery result is not implied by this module proof.

Earlier sections and exports below remain historical evidence.
