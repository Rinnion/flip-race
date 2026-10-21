# Mixed course authoring proof

The MIX-02 course is `Assets/Scripts/Core/TracksMixed.cs`, ID `mixed-tricks`. It has one physical geometry for both immutable presets. The game does not read the safe/full/negative witness arrays. This tool uses the production Core and the deep snapshot / event-aware scheduler in `TrackProofControl/Harness.cs`; there is no independent replacement physics model.

Run from the Unity project directory:

```powershell
dotnet run --project Tools/MixedCourseCompose/MixedCourseCompose.csproj -c Release -- --verify
dotnet run --project Tools/MixedCourseCompose/MixedCourseCompose.csproj -c Release -- --semantics
dotnet run --project Tools/MixedCourseCompose/MixedCourseCompose.csproj -c Release -- --recovery
dotnet run --project Tools/MixedCourseCompose/MixedCourseCompose.csproj -c Release -- --bypass
```

The final source SHA256 is `01C67639E567F2325D668678786AE5EAFDB5352C8204D8CC876EF288645210E0`. The only change from the verified r2 checkpoint `4658023A…` is a neutral source comment; physical geometry, parameters and all routes are unchanged.

- `--verify`: **35,701 assertions**, both presets × safe/full/first trap/second trap/both traps; six frame steps (30/60/144 Hz, 0.317/2.5/100 s). Every accepted input is individually moved by ±60 ms for safe and ±25 ms for full/trap while all other inputs retain their authored distance triggers. Both players finish with zero collisions, expected rewards, and naturally empty ordinary-effect accounting. Finish times are 36 / 33.2 / 33.5 / 33.8 seconds.
- `--semantics`: **981 assertions**, rerun on the final source with both negatives. Confirms same-wall small/straight motion, actual One charge usage, opposite-wall exit from the large stone, actual Stone support on both landing marks, and speed 330 at the second earned-chain action. It also compares the planner's inputs with the public witness arrays. The large landing is sampled at X5326, after actual contact; X5300 alone was too early for Main.
- `--recovery`: **244 actual first-hit states**, all with a legal witness after the true remaining penalty plus 150 ms reaction. Perturbations are one input at ±160/400/640 ms, safe/full and both profiles. Completion means actual arrival beyond the next obstacle's whole union +15 world units (or the tape if no obstacle remains), with no new collision. A finite stay/two-input search found 239; five identical Main low-gate states needed four actions through two real Wall landings. The independently checked adaptive version is in `mixed-course-recovery-targeted.json`. This is bounded evidence, not a claim about every possible error history.
- `--bypass`: **70 complete symmetric games**: no input, one early input, early transfer to the opposite wall, and eight fixed periods × four phases for each preset. All incur an actual collision. This finite controller set is not an exhaustive optimal-route proof.

Current exports are `Verification/mixed-course-r2-routes.json`, `mixed-course-semantics-r2.json`, `mixed-course-recovery-r2.json`, and `mixed-course-bypass-r2.json`. The r1 exports remain historical checkpoints. `mixed-course-windows-r2.json` is the positive-only checkpoint, before the two negatives were added; use `mixed-course-r2-routes.json` for final candidate geometry and all ten route variants.

The short straight-gate proof (including 110/220/330 and real effect transitions), MIX-01 stage proof, independent full-course audit, catalogue checks and browser tests are separate scopes. See `Verification/MIX-COURSE-AUTHOR.md` for the final episode design and honest safe-route timing limits.
