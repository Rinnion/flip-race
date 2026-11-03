# Hidden obstacle core checks

Run `dotnet run --project Tools/HiddenObstacleCheck/HiddenObstacleCheck.csproj` from the Unity project. No Unity process or packages are required.

HID-01 checks actual Press/Advance motion, all five warning delays, swept real collisions and landing rewards, body postponement and departing/returning contact, pause/countdown/retry, independent forecast state, 2–4-player array permutations under both camera types, terminal participants, airborne players, bonus-driven reveal crossing, visible gates and unchanged hidden metadata on old tracks.

Only tracks containing hidden ordinary stones use global world steps of at most 1/240 s. Body-clear recognition can lag by up to 4.167 ms. Reveal is observed at the exact crossing inside existing analytic movement segments, reducing candidates by timestamp and then player ID; recorded warning duration uses exact deadline cuts. Core motion, collisions, landing planes, rewards, recovery and legacy track stepping retain the existing analytic implementation. Visual acceptance and the exported build are separate checks.
