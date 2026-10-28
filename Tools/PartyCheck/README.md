# V5 Party Core checks

Run from `flip-race-unity`:

```powershell
dotnet run --project Tools/PartyCheck/PartyCheck.csproj
```

Offline .NET 8 runner; no Unity API or dependency packages. It compiles the actual Core sources and currently checks 512 assertions for PARTY-00/01/02/04/05/06.

Coverage: 2/3/4 participants, all-ready countdown/remainder, character retention and clean rematch, ties/winner preservation, all-terminal completion without old grace timer, lone runner continuation, all eliminated without a winner; immutable logical window, L/36 scroll, pause/countdown/finished time, continued scroll beyond 36 seconds; exact/partial/full body boundary and no right-side elimination; swept elimination during collision recovery and negative effect, effect expiry, personal coins and real boosts for four players, rejection of terminal input/rewards/motion; Chosen/FastFlip/manual flight input across four slots; mixed player array order and dt 1 second/1:30/1:240, finish before/at/after elimination, finish tie with obstacle contact; lagger selection among Running participants and no elimination in that alternative.

This proves deterministic simulation behavior. It does not establish browser input routing, actual touchscreen behavior, camera framing/artwork, WebGL compilation or ZIP correctness; those remain delivery checks.

Core API: `RaceSimulation.CreateParty(track, flight, count, camera, cameraStartLeft, windowWidth)` takes explicit camera configuration. `GameState.IsParty`, `PartyCamera`, `PartyCameraStartLeft`, `PartyWindowWidth`, `PartyCameraSpeed` are readonly fields. `PartyCameraLeft` is the shared logical left boundary. `PlayerState.Status` is Running/Finished/Eliminated; `TerminalTime` and `FinishTime` use active race time. Preexisting CreateGame/CreateTrial APIs remain two-player.

Legacy regression used without regenerating its baseline:

```powershell
dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- Tools/engine-parity-fixtures.tsv.gz
```

Passed all built-in suites and JS/C# differential comparison: 40,228 snapshots / 1,448,208 state fields, numeric tolerance below 1e-8. The fixture is tolerance-based; it is not itself a bit-for-bit proof. Legacy motion arithmetic and the legacy grace path remain unchanged.
