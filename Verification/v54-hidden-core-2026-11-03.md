# HID-01: shared hidden-stone core verification

Verified 3 November 2026 on the integrated working tree based on `3ba05249d719790b79c630711e3a74cf7e8f49d2`. This is core evidence for the [5.4 delivery contract](../../flip-race/docs/flip-race-hidden-tracks-delivery-2026-11-03.md), not browser/build acceptance. Implementation and checks used Sol/high.

## Changed files and decisions

- `Assets/Scripts/Core/RaceSimulation.cs`: immutable `Obstacle.Hidden`, per-game warning setting/state, retry reset, hidden filtering in both swept collision and marked landing, shared hidden-only world stepping. Separate `world` and optional `party` arguments preserve legacy trial finish/elimination behavior.
- `Assets/Scripts/Core/HiddenObstacles.cs` and `.meta`: ordinary floor/ceiling stones only; gates remain solid. Shared warning starts from the first active player's exact 440-unit crossing. Analytic movement reports constant-speed segments, including actual reward/finish speed changes. Earliest timestamp wins; exact ties use stable player ID, independent of player and obstacle array order.
- `Assets/Scripts/Core/RaceSnapshot.cs`: independent map and mutable hidden-state entries. Track geometry remains shared read-only.
- `Tools/HiddenObstacleCheck/`: offline executable checks and README.

Warning defaults to 0.8 s; supported values are 0.4/0.6/0.8/1.0/1.2 s. Warning is intangible and cannot support or award landing. Deadline cuts are exact. At the deadline any active body's collision envelope postpones activation globally; mere flight does not. Inclusive body contacts postpone until actual separation, preventing a false hit while departing. A later return can hit normally. Solid remains permanent. Retry clears reveal states and preserves the selected duration; countdown and pause do not advance the timeline.

Only hidden tracks use shared steps of at most 1/240 s. Body-clear recognition can lag by at most 4.167 ms; trigger timestamps and warning deadlines are analytic/exact. This bounded technical approximation is the deliberate quick-playable MVP choice. No flight parameters, hit recovery, respawn rules, old geometry, bot controller or old-track stepping changed.

## Executed checks

All commands ran from `flip-race-unity`:

| Command | Result |
| --- | --- |
| `dotnet run --project Tools/HiddenObstacleCheck/HiddenObstacleCheck.csproj` | PASS: 13 scenario groups / 1005 assertions |
| `dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- --flight` | PASS: 12 scenarios / 2424 assertions |
| `dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- --offset-gates` | PASS: 4 scenarios / 2960 assertions |
| `dotnet run --no-build --project Tools/ParityRunner/ParityRunner.csproj -- --modes` | PASS: 4 scenarios / 1190 assertions |
| `dotnet run --no-build --project Tools/ParityRunner/ParityRunner.csproj -- --control` | PASS: 19 scenarios / 2166 assertions |
| `dotnet run --no-build --project Tools/ParityRunner/ParityRunner.csproj -- --finish` | PASS: 10 scenarios / 2054 assertions |
| `dotnet run --no-build --project Tools/ParityRunner/ParityRunner.csproj -- --v4-post-hit-ring` | PASS: 12 assertions |
| `git diff --check -- Assets/Scripts/Core/RaceSimulation.cs Assets/Scripts/Core/RaceSnapshot.cs` | PASS; existing CRLF normalization notice only |

Targeted groups replay actual `Press`/`Advance` physics at 30/60/144 fps and large 0.317/10 s requested steps. They cover all five delays, exact deadline boundaries, real solid hits, floor/ceiling warning pass-through without landing, real solid landings with speed reward, body-postponed departure then return hit, pause/countdown/retry, independent forecast advance, 2–4 players under both camera types, reversed player/obstacle arrays, sub-tick competing crossings, inactive bodies, flight outside the stone, actual swept bonus-driven threshold crossing, gates, legacy non-party trials, and hidden-free metadata/state on all five original full tracks.

## Handoff and remaining acceptance

HID-01 core is implemented and verified. CONTROL/GATE/REWARD/FINISH preserve their targeted existing checks; SOLO/shared race and forecast behavior have additional new checks. BOT's old-track core path receives no hidden world observer and no hidden substeps. This report does not claim a new bot benchmark, complete fixture replay, visual acceptance, mobile feel, exported WebGL, ZIP correctness, or new-course playability. Root owns integration, current feature documentation, STATUS, browser/build checks and final acceptance. No commits or Unity builds were made by this specialist.

The sandbox initially could not create the verification directory and denied dotnet's `obj` temporary-file write. The ordinary scoped `require_escalated` review allowed directory creation and offline checks. No policy refusal occurred during this specialist run and no permissions were changed.
