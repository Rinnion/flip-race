# Mixed tail bound

Run from the Unity project root after `TracksMixed.Get()` exists:

```powershell
dotnet run --project Tools/MixedTailCheck/MixedTailCheck.csproj
```

Compilation alone is not a course PASS. A successful run writes `Verification/mixed-tail-bound.json` with the exact course/Core/geometry SHA256, every latest release bound, all 2 048 subset checks, worst completion X and margin before X7876. Failures print `NO TAIL PASS` and return a nonzero exit code. The tool does not modify the course or clear gameplay effects.

This is an independent workload bound, not a replay of the author's routes. In distance coordinates a completed positive pulse consumes `330 × 0.6 = 198` units; a negative consumes `110 × 0.6 = 66`. The production queue conserves unfinished work under instant-effect preemption. Recovery stops both distance and effect service. Each reward is awarded at most once.

For each circular reward, the latest possible contact lies at center X plus its radius and the player's radius. For a landing, use the full authored landing interval as a conservative bound (the real stone face can only shorten it). A delayed linked reward can be awarded after contact, so additionally bound its release by `max(ClearX, complete obstacle union right + player radius + epsilon)`.

Move every selected pulse's release to that upper bound, sort by bound and compute `end = max(end, latestRelease) + work`. Delaying arrivals cannot reduce the final completion of a work-conserving queue; service order and preemption do not change total remaining distance-work. Thus the bound covers every possible collection order, including instant effects and cancelled/skipped rewards. Checking every subset also includes unreachable histories, so the result is conservative. No chosen input route or timing sample is assumed.

The proof pins the unchanged 4.6.1 simulation and geometry source hashes, as well as the relevant speed/duration constants. A future change to those sources requires reviewing this argument rather than silently accepting an old model. It checks the 9 positive / 2 negative / 1 finish budget without imposing an old distribution of landing/before/after reward types. The entire expanded obstacle unions and ordinary contact intervals must lie before the final 44 units.
