# Stochastic snapshot check (5.3.0 stage 00)

Run from the Unity repository:

    dotnet run --project Tools/StochasticSnapshotCheck/StochasticSnapshotCheck.csproj

The command writes only `Verification/stochastic-bots-snapshot.json/.txt`. It records Core source SHA-256 values before/after, full-field fingerprints, and reachable scenario evidence. Reflection is confined to this standalone test; production `RaceSnapshot.Clone` uses typed copies and identity memoization.

`RaceSnapshot.Clone(GameState)` preserves all scalar state through two internal memberwise shells and then replaces every mutable timeline reference. Player arrays, personal sets, FIFO queues, each queued/active effect, and event records are independent of source and sibling branches. Reference identity inside a cloned graph is retained, including `LastInstant == LastSuccess`, duplicate objects and cross-player aliases. Set equality comparers are retained.

Track geometry, reward definitions, cues and route arrays are **shared read-only race inputs**. Callers must freeze these after race creation, including forecast callers. `FlightSettings`, `MotionProfile`, `FlightMeasurement`, and `BotPlan` are immutable shared values. `BotPlan.Distances` is defensive. No controller or historical action tape is activated by this check. The test fingerprints shared input graphs after ordinary Press/Advance forecasts and authored runs to detect mutations.

The reflection schema fails on any unknown mutable/reference field in GameState/PlayerState, any changed manually copied child layout, or any non-readonly field in shared immutable scalar classes. New scalar timeline fields are automatically copied by the shells and compared by the reflection fingerprints.

Structural fixtures deliberately fill fields and unusual aliases, and are marked separately from physically reachable scenarios. Actual scenarios use ordinary readiness, Press and Advance: countdown, flight, recovery, pending and failed linked rewards, untouched post-hit collection, FIFO/instant preemption with suspended partial slow, merged instant pulses, coins, active finish dash, terminal ties, left-only elimination, both cameras, and all five authored tracks with both profiles. Different A-instant/B-wait branches are evaluated in both orders and must yield identical per-branch outcomes without changing their source. Bot metadata fixtures use an isolated empty test plan solely to check readonly flags and a terminal bot while the human continues; no old catalog is initialized.

Pause tests set the existing Phase shell, as presentation does, then call ordinary Press/Advance. Synthetic field writes are structural tests and are never described as reachable actions. This is not a stochastic policy acceptance, visual/touch test, or Unity/WebGL frame-budget measurement. The allocation/timing sample is a managed desktop result only. Full delivery regressions remain the coordinator's responsibility.