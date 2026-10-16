# CONTROL track proof

**CONTROL-03F:** the final reward is now a precise center-X plane at 4576, center-Y range 52–68. It gives speed 440 for the remaining 44 units and a real 0.1-second advantage. The old circular instant and its results are preserved separately in [pre03f](pre03f/STATUS.md).

This standalone .NET tool compiles the production `Assets/Scripts/Core/*.cs` without Unity. It does not modify production physics or contain a second motion model.

CONTROL-01, CONTROL-02 and the free browser gate passed before runtime geometry was authored. `TracksControl.cs` is now frozen; [REPORT.md](REPORT.md) records the geometry, parameters, results and limits.

Final complete command, from the Unity project root:

```powershell
dotnet run --project Tools/TrackProofControl/TrackProofControl.csproj -- --verify
```

Result: **CONTROL TRACKS PASS 20451 checks**. Full output: [last-proof.txt](last-proof.txt). Exact browser input distances, actual input times/support, reward timestamps and finish metrics: [routes.json](routes.json).

The command covers common safe/ideal/intentional-trap routes in all four modes, stable landing/run/departure/walkoff on both sides, actual input timing windows, sampled recovery and the separate unlimited height passages. CONTROL-03F adds exact-plane replay, separate finish accounting, a clean-approach bound over all eight prior reward subsets and final-only duels in both player assignments. Step limits include 30/60/144 Hz, 0.317, 2.5 and 100 seconds.

Individual stages are `--comparison`, `--windows`, `--recovery`, `--unlimited` and `--finish`. `--export` refreshes `routes.json` using production simulation; run it from the Unity project root.

Observed checkpoints, before the final CONTROL-02 freeze:

- `--preparation`: PASS 327 checks.
- `--landing-fixtures`, after engine CONTROL-01 green: PASS 377 checks.
- `--controls-open`, early signal on the new production API: PASS 775 checks. All four modes complete the unchanged runway in 10 seconds with five accepted supported departures, no hits and no rewards, at five simulation step limits. This does not test air-input rules or replace the engine/browser gates.

See [DESIGN-NOTES.md](DESIGN-NOTES.md) for historical preliminary calculations. The final geometry and measured results are in [REPORT.md](REPORT.md).

From the Unity project root:

```powershell
dotnet run --project Tools/TrackProofControl/TrackProofControl.csproj -- --preparation
```

Preparation checks the scheduler against the existing empty 10-second runway and checks that snapshots deep-copy mutable players, explicit support, effect pulse accounting, queues, sets and event records. Immutable profiles and geometry are shared. Unknown mutable reference types fail explicitly.

After the relevant engine gate, additional commands are available:

```powershell
dotnet run --project Tools/TrackProofControl/TrackProofControl.csproj -- --landing-fixtures
dotnet run --project Tools/TrackProofControl/TrackProofControl.csproj -- --controls-open
```

The landing fixtures are tool-only: first one floor stone, then its ceiling mirror. They launch using real inputs, land at the physical plane, run beyond the narrow marker under the earned acceleration, depart from the stone and walk off its boosted edge. They do not set runtime player coordinates. The open command requires the agreed four production modes and profiles; it does not silently skip an unavailable mode.

The scheduler records actual input time, position, velocity, airborne charge and explicit support. It finds distance-triggered input edges using production simulation and bisection when a new effect changes speed within a frame. Timing perturbations are actual seconds, not distance divided by nominal speed. Both players receive the same test inputs; independent mixed-player verification is a separate audit.

Frozen runtime API:

```csharp
TracksControl.ChallengeId // "control-comparison"
TracksControl.UnlimitedId // "control-passages"
TracksControl.Open()
TracksControl.Get(string id, MotionMode mode)
TracksControl.Challenge(MotionMode mode)
TracksControl.Unlimited()
TracksControl.NegativeRoute(MotionMode mode)
```

`Open()` reuses `TracksV4.Open()`. `Challenge(mode)` returns mode-specific input routes over the same geometry. The UI selects Moderate for GravityAirFlip/GravityUnlimited, Jump for JumpGravityFlip, and Baseline for LinearAirFlip. `Unlimited()` uses GravityUnlimited/Moderate; its identical safe/ideal route has no rewards. Legacy TracksV4 and TracksTune were not changed.

The independent delivery audit uses a different conservative scheduler for asymmetric races, final-only duels, exact landing replays and passage extrema. Browser validation of the finished obstacle courses is a separate CONTROL-04 gate.
