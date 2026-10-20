# 4.6.1: independent review of the catalogue and finish pointer

20 October 2026. Baseline: released 4.6.0, commit `7a9c173b306b16953855c12fb74ce4604eed4338`.

**Read-only review: no actionable findings.** This review changes no production files and does not repeat the route search or the large physics matrix. Browser appearance, the real upgrade, WebGL compilation and packaging are verified separately by the release owner.

## Catalogue and entry paths

- `TracksRhythm.All` contains Cascade only. `RaceTracks.All` keeps the three existing races in their original order and appends Cascade, giving four choices.
- `RaceTracks.NormalizeId` maps `rhythm-dense` and `rhythm-waves` to Cascade. Both relevant string `Get` methods normalize before lookup. Retired indices 4 and 5 resolve to Cascade through `RaceTracks.Get(int)`; valid indices 0–3 retain their identities.
- Startup uses `InitialFlightTrack → RaceTracks.Get(0)`, retaining the former default race. Opening the menu canonicalizes an old in-memory `Game.Track.Id`. Applying a choice normalizes before distinguishing race and practice tracks, so a retired race cannot accidentally fall through to a practice course.
- The menu draws and accepts only `RaceTracks.All.Length` cards. The old card positions for indices 4 and 5 do not become another selection or overlap Apply/Back at the checked 1280×720, 844×390 and 568×320 layouts.
- `CreateSelectedFlight` passes the selected immutable flight settings to the existing Core factory and copies both character indices. Apply still closes the menu and blocks the selection input from leaking into readiness.
- Before a ready/finished input, `Act` replaces only a retired track reference. Core readiness and rematch retain the existing flight settings and characters. Active races and all non-retired tracks are unaffected by that branch.
- Practice IDs remain separate. The old Dense/Waves authoring geometry and witness methods remain available for historical proofs, but are not entries in the playable catalogue.

## Persistence boundary

**4.6.0 did not persist a selected track ID or track index.** Its startup always selected the first catalogue track. Therefore this patch does not claim to migrate a nonexistent saved-track payload: it handles retired IDs and stale indices at the lookup, menu, start and rematch boundaries.

The existing PlayerPrefs keys and custom-flight codec are unchanged. Initialization, custom save, active preset/custom choice and sound storage are unchanged. Character choices remain preserved during in-session selection and rematch, exactly as before; this patch adds no character persistence across reloads.

## Pointer and physical target

The artwork diff is exactly `Rect(ringX - 22, ringY - 8, 22, 16)` → `Rect(ringX - 11, ringY - 8, 11, 16)`. Its tip remains at `(ringX, ringY)`, the height remains 16, and only the tail moves. The texture colours, physical interval ticks, clipping and upper-lane mirror/rotation are unchanged. Collection still hides the same successful zone, and the existing `LastFinishBoost` reaction remains intact.

The simulation, flight presets, old track geometry, full rhythm geometry, negative zones and settings codec have no diff against the release baseline. Consequently the contact plane at `Length - 44`, centre-height window and 440-speed finish reward remain unchanged by this patch. Phone visibility and the rendered response still require the separate browser check; source inspection does not substitute for it.

## Reviewed source hashes

| File | SHA-256 |
| --- | --- |
| `Assets/Scripts/Core/TracksRhythm.cs` | `D1B7AF6109E4443394EE508F54F718A288FEDD6A358408FF43C946ACB5D443B8` |
| `Assets/Scripts/Presentation/FlightSettingsMenu.cs` | `E2C2B5F43D93B8EBE6255F928A051AFEA1572B66892BCF9FD7E5EF2A3A5D8E4A` |
| `Assets/Scripts/Presentation/FlipRaceGame.cs` | `752DD3BC69B8AF6B90A92EACFA277DE32605AFC91E533D1D6BCCAB4BA09011BD` |
| `Assets/Scripts/Presentation/CorridorArtwork.cs` | `07891AD429E5D58B5A37BED4CF358B9DDDAC159C214617424BA3FFA38B3185E6` |

Unchanged physical sources: simulation `AA91B969…`, full rhythm geometry `8B62C325…`, negative zones `75AC8DD7…`, prior full tracks `90125502…`, flight presets `8499B6DC…`.
