# Mixed stage author proof

Run from the Unity project root with .NET 10:

```powershell
dotnet run --project Tools/MixedStageProof -c Release -- --verify
dotnet run --project Tools/MixedStageProof -c Release -- --services
dotnet run --project Tools/MixedStageProof -c Release -- --pulses
dotnet run --project Tools/MixedStageProof -c Release -- --straight
```

`--verify` runs the production short stage in both orders, both mirrors and both unchanged profiles. It checks real inputs/support, two optional rewards, the complete low gate, exact finish/tie, six frame sizes, and individual input shifts (safe ±60 ms; full/quick-return ±25 ms). It exports the flat trace `Verification/mixed-stage-draft.json` and definitive eight-row browser catalogue `Verification/mixed-stage-routes.json`.

`--services` isolates the same obstacles at held horizontal speeds 110/220/330. Only this fixture injects a long active speed effect; vertical motion, inputs, collisions and support use production Core. Rewards are removed. The 110 route deliberately uses a supported bypass instead of claiming that the normal small same-wall jump clears the rock at every speed. These are geometry service checks, not earned-pulse or finish-accounting evidence.

`--pulses` adds one real ring to the reordered stage, retaining its actual obstacles and quick-return route. It verifies earned boost during the low threshold, earned boost/slow at the large launch, real wall/recharge between manoeuvres, six frame sizes, and complete ordinary-effect accounting. No player state, speed or effect is injected in this scope. These additional-ring fixtures are separate from the frozen browser stage, whose early landing boost expires before the low gate.

The shared `TrackProofControl/Harness.cs` schedules real `Press`/`Advance` and uses clones only to locate precise distance triggers. Steps are 1/30, 1/60, 1/144, 0.317, 2.5 and 100 seconds.

Individual timing shifts are not a Cartesian joint-window guarantee. The Fast safe small repeat is airborne, but its nominal trajectory touches the opposite wall inertially before returning to the original wall; that touch does not restore support or the spent repeat.

Final counts and source hashes are recorded in `Verification/MIX-01-ENGINE.md`. Recovery and joint counterexamples belong to the separate independent `Tools/MixedAudit` evidence.

`--straight` is a separate MIX-02 authoring aid, not part of the frozen short-stage geometry. It checks a real straight gate with upper height48, lower height12 and width8. Main uses a natural jump; Fast uses One to return to the original wall. It tests mirrors/six frame sizes, individual ±60 ms at220/330, and ±25 ms for the held110 Fast service. Real instant/negative rings end inside the passage, changing speed330/110 back to220. Results and transferable coordinates are in `Verification/mixed-straight-proof.json` and `MIX-STRAIGHT.md`.

The later full-course scopes use immutable author exports and do not write production files:

```powershell
dotnet run --project Tools/MixedStageProof -c Release -- --negative-proof --source Verification/mixed-course-windows-r2.json
dotnet run --project Tools/MixedStageProof -c Release -- --course-recovery
dotnet run --project Tools/MixedStageProof -c Release -- --course-native
```

The negative proof adds the two proposed zones to the positive-only r2 snapshot, retains the original safe/full windows, and finds single/two-trap full routes. Recovery uses the final ten-route r2 export, replays actual failed prefixes from the author's bounded scan, and checks complete next-obstacle unions. Native checks only the known Main large-pad timing risk at30/32 Hz. Scope, source hashes and limits are in `Verification/MIX-COURSE-ENGINE.md`; these are not exhaustive reachability or joint-jitter proofs.
