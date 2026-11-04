# 5.4.2 — new-track BOT UI handoff

4 November 2026. Implemented and checked with Sol/medium. Source of scope: [V542 STATUS](V542-NEW-TRACK-BOTS-STATUS.md), direct user request to add bots to the three new tracks while retaining the common algorithm. The following final sequential runs were repeated after the adaptive HiddenObstacles.StepLimit fix, on production later committed as `856acc5`; they supersede the earlier 10:00–10:01 UTC run timestamps. Each runner verified its frozen production source snapshot through execution; all four snapshots were also compared against disk afterwards with no mismatch. Final HiddenObstacles.cs SHA256: `07782B10C63D24690F764E663ACECAF678AFF7E62ACAAA05512CBEA2CE011C18`.

| Evidence | Final result |
| --- | --- |
| [New-track BOT UI](v542-new-track-bots-ui.json), `2026-11-04T10:23:43.567842Z` | PASS: 17766 assertions, 12746 production Pointer calls, 750 new-track setups and retries, 50 original-track setups, 18 rejection cases |
| [Hidden UI regression](v542-hidden-ui-regression.json), `2026-11-04T10:24:09.863965Z` | PASS: 1512 assertions, 1680 Pointer calls, 12 layouts, 60 settings cases, 72 party cases |
| [Rethink UI regression](v542-rethink-ui-regression.json), `2026-11-04T10:24:35.8374516Z` | PASS: 630 assertions, 672 Pointer calls, six viewport/DPR configurations, 18 party cases |
| [Stochastic UI regression](v542-stochastic-ui-regression.json), `2026-11-04T10:24:53.1405299Z` | PASS: 124089 assertions, 1304 setups, 2470 layouts, 46059 Update calls, five complete physical Update races and five in-flight pauses |

## Changes and decisions

- `Assets/Scripts/Presentation/FlightSettingsMenu.cs`: menu eligibility and catalogue pointer/render state use the lightweight `StochasticTrackMap.SupportsTrack(id)` predicate. The two original hidden tracks accept both ready profiles; Rethink retains its separate exact-Chosen profile restriction. Unsupported Rethink Fast and all custom BOT choices remain blocked without replacing the map, Game or seed. Human equivalent-Chosen custom remains allowed.
- `Assets/Scripts/Presentation/BotsPresentation.cs`: replaces obsolete “пока без ботов / прежних пяти” messaging with the remaining full-track/profile restrictions. The selected map remains explicit; no fallback to a different map was added.
- New `Tools/NewTrackBotUiCheck/`: a managed production-menu runner reusing the established native environment source transforms. It covers all five new map/profile combinations × five difficulties × five warning choices × six viewport/DPR configurations (568×320 and 1280×720, DPR1/2/3), plus original-map compatibility and profile/custom rejection cases.
- `Tools/HiddenUiCheck/Program.cs` and `Tools/RethinkUiCheck/Program.cs`: only the now-obsolete BOT-denied scenarios intentionally change to successful exact-map selection/Apply and seed behavior. The other selection, layout, SOLO, party, warning and custom/profile checks remain. Their READMEs explain the 5.4.2 expectation change. Historical evidence JSON files were preserved.

No factory overload was added. Core confirmed initial settings/map construction and controller initialization do not advance the world or read warning duration. The existing Apply sequence configures the selected global duration before the first Update; Retry configures the previous duration before controller initialization and any Update. The new matrix explicitly checks `Game.HiddenWarningDuration`, zero elapsed time and zero planning calls immediately after Apply, then through start, first countdown Update and retry. Rethink's authored obstacle durations continue to override the retained global preference; switching back to an ordinary hidden map restores that preference.

## Recorded source identity

Full source snapshots are in each JSON. SHA256 of the UI integration and relevant Core files:

| File | SHA256 |
| --- | --- |
| Presentation/FlightSettingsMenu.cs | `C9492AA9B57C5B05A23E55B84EA6804CEE6D7F7ED7C2D1FCC15E1DB42C9B293F` |
| Presentation/BotsPresentation.cs | `CAC0832E6634998A8EC5D222A7D17D1886B7A1E4869BD8757E8ED4E080015DB3` |
| Core/StochasticTrackMap.cs | `6D999A6D52C8F1A8D113F997CEED5A8F7418250E7B10F7B3E5568074732491B9` |
| Core/RaceSimulation.cs | `A85B9D1F7D417C43038F65829FACD372B224AD809AFC22BF2CBDB3B85D08217C` |
| Core/BotReferenceRoutes.cs | `F7F63D0658089BA8A46B0563D7D232A86C8BF4EB0FE5DB19A663EDB7D12F66DA` |

## Scope of proof and remaining acceptance

The new test calls real production Pointer/menu/Update/Apply methods with only the existing native display/input/clock/preferences/DPR/selection-texture environment seams. Retry cases use synthetic terminal fixtures and establish result routing, fresh seed selection and preserved settings, not full-route bot reachability. The separate five complete races in StochasticUiCheck exercise the original tracks. New-track full gameplay, reward compatibility, hidden forecast independence and execution performance belong to Core/integration evidence.

Root owns actual WebGL/browser menu validation, low-level bot frame performance, build/archive verification, current product documentation and commits. These managed results do not establish native rendering, browser timing or physical-phone usability. Preserve BOT/HIDDEN/CONTROL/REWARD/FINISH/GATE/VIEW/TOUCH/SOLO. No artwork or rendering code changed in this UI scope; the existing [art direction](../../flip-race/docs/flip-race-art-direction.md) and Tanya's authorship remain applicable.

No production file was modified during or after the final frozen runs. No UI defect was found. Bounded preliminary compilation briefly failed while parallel authoring had not yet produced BotReferenceRoutes; it passed once that file existed. Technical workspace directory/generated-file access required scoped elevated commands; no policy rejection occurred. No Unity build, browser control or commit was performed by this specialist.
