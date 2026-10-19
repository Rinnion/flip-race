# MODES-02 — two profiles and preservation of custom settings

19 October 2026. **Source, isolated checks and final browser QA PASS.** This report covers UI/migration; common-geometry proofs are in [MODES-AUDIT.md](MODES-AUDIT.md), actual routes and archive loading in [MODES-BROWSER.md](MODES-BROWSER.md).

## Selection and storage

- ID **10** remains the primary `FlightPresets.Chosen`: Jump / Gravity / unlimited, gravity 662.5, flip impulse 0, jump impulse 350, linear speed 265, linear height 112.
- ID **11** is `FlightPresets.FastFlip`: Flip / Gravity / no air repeat, gravity 2700, flip impulse 35, inactive jump impulse 300, inactive linear speed 530, inactive linear height 112.
- The two visible buttons are `Основной — прыжок и полёт` and `Быстрый переворот`, plus `Свои настройки`. Selecting a button affects the next ready game only and keeps the pending track. Applying preserves both character choices and returns to readiness without starting automatically.
- Old preset buttons, their page, rectangle helpers and pointer branches are removed. The separate previous-track page remains; its unlimited-only section still requires unlimited air repeats. The manual editor, eight-position scales, copy overlay contract and hidden parameter values are retained.
- Storage keys remain `flip-race-flight-choice-v2` and `flip-race-custom-flight-v1`. Active `-1`, `10`, `11` restore unchanged; retired `0..9` and invalid choices become `10`. Clean startup defaults to `10`.
- Valid old custom payloads decode with exactly the same physical fields. Only reset references `0..9` become `10`; reset `10` or `11` stays intact. Initialization saves this canonical metadata migration once. Corrupt custom data is not rewritten during initialization; the in-memory fallback is the primary profile.
- Switching either preset does not overwrite the remembered custom. Manual editing creates a custom draft with reset source `10` or `11`. Explicit reset loads that source. Copying the user's original `1|0|1|0|2700|35|300|530|112|10` preserves its physics and reset target 10; the final field is not the alternative's mode ID.

## Executed checks

```powershell
dotnet run --project Tools/FlightSettingsCheck/FlightSettingsCheck.csproj
dotnet build Tools/FlightUiCompile/FlightUiCompile.csproj --nologo -v minimal
```

**670 assertions / 111 exact physical round trips PASS**. Coverage includes twelve action/law/repeat combinations, all eight scale positions, both current presets, all ten retired metadata references, exact active and inactive alternative values, idempotent migration, custom restoration after switching both presets, immutable active settings, explicit reset and complete copy text under en-US/ru-RU/tr-TR.

Compilation against Unity reference assemblies passed with **0 warnings / 0 errors**. Source search confirms no page-2 preset branch, legacy preset button rectangle or old four-button rectangle helper remains. `git diff --check` passed for changed code and tests. No Unity build was run by this task.

## Browser QA coordinates

All coordinates are CSS pixels relative to the canvas. At 568×320, the existing panel is `[12,28,544,280]`; the new primary button is `[28,88,248,42]`, fast button `[28,139,248,42]`, and custom `[28,200,248,36]`. At 844×390, the panel is `[112,45,620,300]`; buttons are `[128,105,286,42]`, `[128,156,286,42]`, `[128,217,286,36]`.

The three-track buttons and native copy rectangle keep their previous layout. The old left-side legacy-link area below custom is empty and has no handler. A click there must not choose a removed profile or close the menu. Page changes and close/apply retain the existing input-release barrier.

## Executed browser checks

[modes-ui-browser.json](modes-ui-browser.json) preserves 17 observations from the final implementation. Actual migration of old active ID1 selected the primary profile. The old custom payload `1|0|1|1|1000|140|400|265|112|0` retained all physical values and changed only its reset reference to10. This is one real browser migration; all ten retired IDs are covered by the isolated codec tests above.

Fast selection and reload retained every active and inactive value. Selecting/applying/reloading Fast did not overwrite custom; custom subsequently applied and survived reload. Native clipboard text matched the custom copy representation. Editing a draft preserved the active game's settings until Apply; reset to Fast ID11 restored inactive values too. Clicking the disabled linear field left Gravity settings unchanged. Switching tracks preserved Fast, and applying returned to readiness without automatic start. The final primary/Long view was also checked at568×320.

The final main-game smoke confirmed that changing a character resets only that player's readiness, preserves the other player's readiness and permits both players to choose the same character. After clearing the viewport override, the natural656×1012 portrait screen retained the visible version in readiness.

Screenshots: [568×320 menu](modes-menu-568.png), [844×390 menu](modes-menu-844.png), [Fast editor](modes-editor-fast.png), [final ready](modes-final-ready-844.png), [natural portrait](modes-final-portrait.png). Route tests cover both player lanes; [latency evidence](MODES-LATENCY.md) separately records actual character clicks. Source removal of old hitboxes is not presented as a new browser click for every former rectangle. These desktop viewport checks do not establish physical mobile multitouch or device performance.

## Scope boundaries

The new Core change is only the immutable public `FastFlip` preset. No physics, track geometry, mascot artwork, readiness rectangles, Web bridge, build configuration or version source was edited here. `FlipRaceGame.cs` was left untouched; its old-range `legacyControls` initialization evaluates false after the new active-choice migration. The menu also clears that flag on apply.

The final shared track geometry is used for both presets and custom; selecting a track never changes the flight settings. Custom full-track selection is labelled `Свои настройки · исходная трасса`. Both named presets have verified routes on that identical geometry. Arbitrary custom settings do not receive that compatibility guarantee.
