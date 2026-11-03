# Hidden tracks check

Run from `flip-race-unity`:

```powershell
dotnet run --project Tools/HiddenTracksCheck/HiddenTracksCheck.csproj
```

Uses production `RaceSimulation.Press/Advance` and `RaceSnapshot.Clone` for exact distance scheduling. No alternate physics, bot planner, global search or saved bot calibration is involved. Output is `Verification/v54-hidden-tracks-witnesses.json`; a failed assertion exits nonzero.

For both tracks and Chosen/FastFlip it checks clean optional-reward-free completion; a real marked-stone landing and full 0.3-second effect; a clean air-reward route and completed effects; optional finish collection with a real 0.25-second gain; idle and two finite same-wall return attempts; three selected input omissions; finishing after one real hit; all five warning settings; both physical gate parts; independent ±20ms shift of the first input. Landings and input support states are recorded. These finite witnesses do not prove global impossibility of other routes or a timing envelope for the whole course.

`TracksHidden.SafeRoute`, `RewardRoute`, `AirRoute`, `FinishRoute` are author witnesses only. They are not bot references. Intercepts Chosen air approach can also take the landing reward: compatible mixed collection is intentional. Fast's independent Intercepts pad witness uses the third series to avoid the first air pair.

Human readability, challenge and fun, phone timing comfort, and final build visuals require a playtest. The tests do not establish those qualities.
