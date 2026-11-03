# 5.4.1 Rethink UI handoff

3 November 2026. Implementation and managed checks: Sol/medium. Production Core/Presentation/Web were frozen by root before the two final runs. Production files were not changed during or after those runs. Both runners verified every recorded production source hash through execution; key presentation and track hashes were also compared against disk afterwards.

| Final evidence | Result |
| --- | --- |
| [Rethink UI](v541-rethink-ui.json), `2026-11-03T19:18:39.5519977Z` | PASS: 630 assertions, 678 production Pointer calls, six viewport/DPR configurations, 18 party cases |
| [Hidden UI regression](v541-hidden-ui-regression.json), `2026-11-03T19:18:54.5205894Z` | PASS: 1488 assertions, 1704 Pointer calls, 12 layout/profile/DPR configurations, 60 warning settings cases, 72 party cases |

## Delivered presentation

`FlightSettingsMenu.cs` shows eight full-track cards in the existing three-column grid. Rethink's label is “Передумай · Основной”; Track.Name remains unchanged. Unsupported Fast or modified custom choices cannot Apply or silently change the profile. Explicit Chosen and exactly equivalent Chosen custom work. The authored timing row says “Появление камней задано трассой” and ignores taps; the five global warning selections survive moving between Rethink and the original hidden maps. Rethink is explicitly unsupported in BOT alongside the two prior hidden tracks; explicit old-map BOT selection remains available.

`HiddenObstaclePresentation.cs`, `CorridorArtwork.cs`, and `SharedFieldArtwork.cs` draw the same exact physical band contour immediately during Warning, plus the existing stone texture with smoothstep alpha 0.12→0.68 from Core WarningProgress. `Rock` now accepts optional warning opacity; its original solid calls and artwork remain intact. The warning path returns before solid landing/decorative marks. It remains translucent at progress 1 while activation is postponed. GUI.color is restored. Geometry, cameras, input physics and both gate parts are unchanged.

Managed tests execute production menu/Pointer/Update/Apply methods with the established native environment seams. They establish eight whole disjoint 44-unit targets at 568×320 and 1280×720, DPR1/2/3; Fast/custom rejection preserving Game, map and seed; Chosen and equivalent custom success; Rethink SOLO/Party2–4; BOT rejection and explicit old-map success; authored/global warning roundtrips. The previous seven track scenarios remain in HiddenUiCheck without weakened assertions, except its catalogue count now expects eight unique entries.

## Source hashes

Full snapshots are in each JSON. SHA256 for key integration files:

| File | SHA256 |
| --- | --- |
| Core/HiddenObstacles.cs | `2211B3252E0D458E22FC81AB57A7359BC1594DCFDA35833B8D36F7D7DFEAC53D` |
| Core/TracksRethink.cs | `502A479F66D6E7AA6F82D082B9ACA2D6E3332290797BEB7105380521532EA488` |
| Presentation/CorridorArtwork.cs | `D6D6C3AD846645725DB4E2703113DE0F108764352E6B897F767FECB7470A704B` |
| Presentation/FlightSettingsMenu.cs | `736D6D84C96F7959D9A1EA35E2292DCA006951133687AE91BCAFECBF041B8E4D` |
| Presentation/HiddenObstaclePresentation.cs | `66E4C2A721FD6D320AF1E0664FE5C838A0E93F69DFD45FB78A81D32DE3794DA2` |
| Presentation/SharedFieldArtwork.cs | `A93F065F3CB4CF107BD410B1735244C9869768DFC6965FC770D68500738917B3` |

The Rethink wrapper initially included its prerequisite build stdout before JSON. That prefix was removed from the saved report without repeating the successful test; the wrapper now uses Out-Null instead of Out-Host for prerequisite build output. No production correction was necessary. Scoped elevated commands handled previously observed technical generated-file Access denied; no policy rejection occurred.

## Remaining acceptance

Root owns Unity build, actual WebGL screenshots, native pointer input, font fit and fade/contour readability on both renderers, including pause and postponed activation. Managed checks do not establish visual appearance, full-route playability, physical-phone usability or artistic approval. Preserve HIDDEN/CONTROL/GATE/REWARD/TOUCH/VIEW and [Flip Race art direction](../../flip-race/docs/flip-race-art-direction.md), including Tanya's existing artwork and authorship. No Unity build, browser session or commit was performed by this specialist.
