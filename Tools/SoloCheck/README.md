# SOLO Core checks

Run from `flip-race-unity`:

```powershell
dotnet run --project Tools/SoloCheck/SoloCheck.csproj -- Verification/solo-core.json
```

The report argument is optional. Offline .NET 8 runner, compiling the actual Core sources without Unity APIs or external packages. Verified on 28 October 2026: 84 full-track cases, 59,482 complete personal-state comparisons, 1,478 identical input actions, 7,662,156 individual assertions. The high assertion count comes from recursive field comparisons, not independent playthroughs.

`RaceSimulation.CreateSolo(track, flight, camera, cameraStartLeft, windowWidth)` creates exactly one participant with `Mode = "solo"`, readonly `IsSolo = true`, and `IsParty = false`. `UsesSharedRaceCamera` selects the same camera, elimination and all-terminal simulation path for SOLO and company. Existing `CreateParty` still accepts only 2–4 participants; legacy APIs retain two participants.

Coverage for SOLO-00/01/02/03/06 and preserved VIEW/CONTROL/REWARD/FINISH/GATE behavior:

- All five current full tracks, Chosen/FastFlip and Constant/Lagger cameras. Idle, safe, full-reward and negative input witnesses; Mixed also uses its two-coin witness. The same generated sequence of timestep values and input edges is supplied to SOLO and company slot 0; companion inputs are identical so they cannot shift the Lagger camera. Party sizes rotate through 2/3/4.
- Every public personal field is compared recursively at each snapshot, including collision/support/flight state, finish/elimination timestamps, all reward ID sets, last-event records, active effects and ordered pending effects. Doubles use an absolute 1e-8 tolerance. This is deterministic tolerance-based equivalence, not a claim of bitwise identity.
- Exact left body boundary, partial body and no right-side elimination; finish before/at/after elimination at dt 1, 1/30 and 1/240; exact terminal time and immediate outcome without the legacy 3.5-second grace period; frozen result camera and motion.
- Single ready edge, countdown remainder, pause and invalid delta handling, invalid slots, immutable window settings, L/36 beyond 36 seconds and no elimination with Lagger.
- Separate fixtures keep a company companion alive after slot 0 has finished or been eliminated. SOLO ends immediately while company continues; the complete terminal personal state stays identical despite different global elapsed times, including beyond the old grace interval.
- A real coin/boost/slow attempt followed by a dirty terminal-state repeat is compared field by field with a fresh selected-character attempt. Track, settings and character are retained; all personal fields, queues, rewards, timers and result reset. Null/invalid configuration and legacy/company APIs are checked.

The saved evidence includes 74 finishes and 10 eliminations; collision, positive effect, negative effect, coin and finish-dash cases all occur. Full reward witnesses with Lagger completed all five tracks under both presets with zero collisions and nine ordinary boosts. The matrix compares preservation; existing dedicated feature suites remain the independent source for intended physics/geometry.

Regression run after the Core edit:

```powershell
dotnet run --project Tools/PartyCheck/PartyCheck.csproj
dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- Tools/engine-parity-fixtures.tsv.gz
```

PartyCheck passed 512 assertions. ParityRunner passed all built-in suites and the unchanged JS/C# baseline: 40,228 snapshots / 1,448,208 fields, numeric tolerance below 1e-8. No historical report or fixture was overwritten. New run evidence is `Verification/solo-core.json` and `Verification/solo-core-checks.log`.

These are pure Core checks. They do not prove UI/input routing, persistence behavior, layout/artwork, WebGL compilation or ZIP correctness; those remain integration and delivery checks. Source request and acceptance context: `flip-race/docs/flip-race-v5-solo-delivery-2026-10-28.md`; root delivery status: `Verification/V5-SOLO-STATUS.md`.
