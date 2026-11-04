# 5.4.3 JSON track loading and import UI

4 November 2026. Sol/medium implementation. [Delivery STATUS](V543-JSON-TRACKS-STATUS.md). Production Core and UI were frozen before the final [targeted evidence](v543-json-tracks-ui.json), timestamp `2026-11-04T11:43:53.6933757Z`: **PASS, 134 managed assertions, 161 production Pointer calls, all 15 builtin BOT pairs; actual HTML template bridge PASS, 12 assertions.** All source/data/template/package hashes were checked through the run and compared with disk afterwards. No Unity build or native browser control was performed by this specialist.

## Delivered behavior

- `FlipRaceGame.cs` defers initial Game creation until the JSON catalogue is installed. Update safely waits without Game. `JsonTracksPresentation.cs` downloads `StreamingAssets/Tracks/index.json` and every listed file with UnityWebRequest, then calls the atomic Core installer. No C# full-track fallback is constructed on HTTP, manifest or validation failure. A visible loading/error overlay and explicit retry remain until catalogue and Unity startup are both ready. Editor uses file URIs for the same authored files.
- `FlightSettingsMenu.cs` preserves the existing eight card positions and uses the ninth grid cell for import. Additional tracks occupy pages of eight, with 44-unit footer arrows and Apply. Selection, authored timing and profile restrictions use Core metadata helpers. `BotsPresentation.cs` explains the remaining imported/reference/profile restrictions. No profile or mode is changed automatically.
- The actual HTML `#import-track` button directly opens `#track-json-file` under browser user activation. File text is passed unmodified to `FlipRace.ImportTrackFile`; no JsonUtility envelope, URL input, network upload or solver is involved. File.size is checked against the shared 2 MiB budget before reading, and Core validates again. Error/cancel/result messages use textContent; managed error page 10 provides recovery. The import control and success notice state that tracks exist only in this session. Late callbacks cannot import or suppress gameplay input after the menu closes.
- `TrackFileBridge.cs`, `TrackFiles.jslib`, `FlipDiagnostics.cs` and the HTML template connect startup/import states. No fourth diagnostic MutationObserver was added: positioning shares the existing copy-button callback and its parsed snapshot. Packages add only the built-in UnityWebRequest module.

The special `FLIP_SCREEN_STAGE` branch retains its explicitly selected C# `TracksMixed.Get()` scene setup after catalogue loading, as before. The ordinary trial target uses JSON-loaded catalogue geometry. This stage branch is outside the normal eight-track runtime migration and has not been claimed as separately verified here.

## Browser handoff

- Before Game exists: `canvas.dataset.flipCatalogState` contains `{phase,message}`. Test missing index, missing listed file and malformed package: the overlay must remain visible, with a meaningful error and Retry. Restoring files and clicking Retry must create the first Game only after complete success.
- Ready diagnostics include `trackCatalogState`, `trackCatalogCount`, `trackCatalogPage`, `trackCatalogPages`, `pendingTrackId`, `currentTrackId`, and `trackImportVisible/X/Y/Width/Height/Status`. `canvas.dataset.flipTrackImport` records the latest import result.
- Click the actual import overlay and use the documented browser filechooser event/setFiles path. A valid unique package adds/selects its page/card without replacing Game until Apply. Duplicate/malformed input opens error page 10; Back returns to the catalogue. Cancellation keeps the previous selection. The supplied offline sample is `Tools/TrackCatalogExport/Samples/my-mixed-track.json`.
- Verify desktop and 568×320 card/font/overlay alignment, page arrows, loading/error/retry, native file reading, imported SOLO/party/retry, chosen-only/authored metadata and explicit BOT rejection. Build/archive staging must include index and every listed JSON. Root owns these actual browser/build checks.

## What the target checks establish

The managed tool reuses the established clock/display/input/preferences/DPR/selection-texture seams. Production Pointer/Update/Apply/import bodies remain unchanged; only native status publication is disabled by `FLIP_MANAGED_CHECK`. It establishes the pre-fetch/error Game gate, exact eight-card geometry, ninth import cell, dynamic pages, session provenance/notice, error recovery without catalogue/Game replacement, metadata restrictions, imported SOLO/retry/party, imported BOT rejection without seed consumption, all 15 JSON builtin bot pairs and retained C# practice profiles. Retry uses an explicit synthetic terminal fixture, not a full imported-track gameplay proof.

The JavaScript test executes the actual production HTML script in a bounded DOM/file-reader environment. It establishes Unity/catalogue boot gating, literal errors, retry routing, import positioning, synchronous file-input activation, raw text forwarding, pre-read size rejection, cancellation/read failure and three observers. It does not replace actual URL fetch, native chooser, rendering, browser performance or physical-phone validation. Core owns round-trip geometry, reference trust and bad-data proofs.

## Source identity and files

Full hashes are in the JSON. Key SHA256 values:

| File | SHA256 |
| --- | --- |
| Presentation/FlipRaceGame.cs | `D0472D6838515243C396ED797FDD3584114D6CF3687A702A50E3C3D655B2DA83` |
| Presentation/FlightSettingsMenu.cs | `1BE1113B41F000910ADFF82214274AFD087C9A94338E0F60BF39AC3F7ADC42FC` |
| Presentation/JsonTracksPresentation.cs | `C21AE829273FD3D08388F9E2370B4C68804901CD017D2B36CBB5B9A89476769A` |
| Core/TrackCatalog.cs | `A142B8F06D8464AFFF0A8D135EFCBAB97B8A5E40A31E6BEADDF77BE69BA91A0C` |
| StreamingAssets/Tracks/index.json | `0F8697F7CA54095AE5CF18AFD340A78C6AF5C0A222136486AC7A00D4804CB8F6` |
| WebGLTemplates/Flip/index.html | `7BC4452CCC87E1D00A7FF6C757EC860981968841EA87BAEABAA93AEE505299AA` |

Owned changes: Presentation `FlipRaceGame.cs`, `FlightSettingsMenu.cs`, `BotsPresentation.cs`, new `JsonTracksPresentation.cs/.meta`; Web `FlipDiagnostics.cs`, new `TrackFileBridge.cs/.meta`; new `Plugins/WebGL/TrackFiles.jslib/.meta`; template HTML; package manifest/lock; new `Tools/JsonTracksUiCheck/`; this handoff and JSON evidence. Core and authored JSON were changed by their separate owners. Existing historical UI matrices were not expanded or rerun. Bounded compile and final target run passed; no policy rejection, commit or unrelated file edit occurred. Technical directory/generated-file access used scoped elevated commands after Access denied.

Preserve GATE/HIDDEN/REWARD/FINISH/CONTROL/BOT/SOLO/VIEW/TOUCH. Existing artwork and [art direction](../../flip-race/docs/flip-race-art-direction.md), including Tanya's authorship, remain applicable; no artwork was generated or edited.
