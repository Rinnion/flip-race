# New bot reference check

Run from `flip-race-unity`:

```powershell
dotnet run --project Tools/NewBotReferenceCheck/NewBotReferenceCheck.csproj
```

The original 5.4.2 proof recorded 165 assertions across 26 complete replays. The current run also includes Rethink / FastFlip. The default command writes `Verification/v542-new-bot-reference-proof.json` in its working directory.

The four Intercepts and Manifestation pairs each replay the same authored reference at warning settings 0.4/0.6/0.8/1.0/1.2 seconds. Rethink replays both profiles with its authored per-stone 0.35-second warnings. Six more replays start every hidden stone in the Solid state as a conservative fixture. Actual human-ahead shared-world, postponed activation, forecasts and stochastic-controller behavior belong to the Core integration checks.

Every replay must finish clean, collect exactly the selected compatible raw reward IDs, complete all ordinary effects, serve the full finish effect, and match `36 − BonusGain`. No physical reward is removed or disabled. Ordinary rewards contribute 66 real distance units/0.3 seconds each; finish contributes 55/0.25. The scheduler uses only production `Press`, `Advance` and independent snapshots for exact input-distance scheduling.

## Selected references

| Track/profile | Ordinary rewards | B | Clean time |
| --- | ---: | ---: | ---: |
| Intercepts / Chosen | 8 | 2.65 s | 33.35 s |
| Intercepts / FastFlip | 10, all | 3.25 s | 32.75 s |
| Manifestation / Chosen | 6, all | 2.05 s | 33.95 s |
| Manifestation / FastFlip | 6, all | 2.05 s | 33.95 s |
| Rethink / Chosen | 3, all | 1.15 s | 34.85 s |
| Rethink / FastFlip | 3, all | 1.15 s | 34.85 s |

Each also includes the full finish target. Intercepts Chosen excludes only `hidden-intercepts-air-0` and `hidden-intercepts-air-1`; it collects both nearby pairs and all three landing rewards, then the final post-air reward. Those first two post-air additions conflict with the following staggered gate in the bounded mixed-trajectory attempts. This is the best compatible clean witness found, not a global optimality proof.

## Bounded authoring evidence

`--compatibility` reproduces the small local experiment in `Verification/v542-new-bot-reference-compatibility.json`: 20 Chosen post-air/next-gate timing pairs and nine Fast first-launch/return pairs. It found a clean extra Fast landing; mirroring that verified local return produced the final all-ten reference. The saved experiment reconstructs its original eight-reward Fast baseline explicitly. No further searches or statistical calibration are part of the default check.

The default check validates references, not all five difficulty levels or the runtime planner. Master/controller, seeded lower difficulties, actual human-leading races, old-track regression and exported WebGL/browser/ZIP acceptance remain separate integration checks.

## Rethink / FastFlip with the shipped catalogue

```powershell
$old = Join-Path $env:TEMP 'rethink-5.8.json'
git show '892005d:Assets/StreamingAssets/Tracks/rethink.track.json' | Set-Content -LiteralPath $old -Encoding utf8
dotnet run -c Release --project Tools/NewBotReferenceCheck -- --rethink-fast $old
```

The focused check compares the old JSON track and Chosen reference, verifies the new policy and authored warnings, and replays both references through the proof used for prepared tracks. It exercises human company readiness with both cameras, all legacy bot levels on FastFlip, and the prestart Master with surface acceleration off and on. An optional final argument saves a JSON report.
