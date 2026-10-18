# 4.4.0 trial UI — source and compile checkpoint

18 October 2026. **Code/compile checks passed; browser checks below are pending.** Full-track geometry and artwork are verified separately. This file does not claim three complete tracks or browser playthroughs.

## Implemented behavior

- Main menu keeps the four previous preset buttons and adds a separate `Профиль трасс` button at index 10. It uses `FlightPresets.Chosen` directly: jump 350, gravity 662.5, unlimited repeats, inactive flip impulse 0.
- `TracksFull.All` supplies the full-track buttons. Choosing a track changes only the pending track ID; choosing a preset changes only the next settings. Applying creates a ready game and preserves both selected characters. Closing leaves the active game intact.
- The first new-version choice uses `flip-race-flight-choice-v2`, default 10. Old indexes 0–9 remain unchanged; `flip-race-custom-flight-v1` remains intact. Applied custom/preset choice reloads independently of the remembered custom data. Track selection is not persisted: startup selects the first full track. While the full-track API is still empty during development, startup temporarily uses `TracksFull.Practice()`.
- A separate previous-sections page retains free flight, control landings, unlimited passages, the tuning trial, the earlier trial and the short full-track prototype. Older six physics presets remain separately available. Passages require unlimited repeats.
- Ready-state taps select a character only inside its card, or set readiness only inside the explicit ready button. A gap or margin returns without `Act`. A/L readiness remains supported. Selecting any character resets only that player's readiness; both players may select the same character.
- Drawing and hit testing share the same card/ready rectangles. Upper-lane screen conversion is `(lane.xMax−local.xMax, lane.yMax−local.yMax)`. Portrait and modal input barriers precede lane input. Trial-menu routing precedes the old hidden `trackClose` rectangle.
- The version comes from `Application.version` and is drawn last, after lanes and all overlays. Compact display uses a 10px version label beside `флип!`, including during a race.

## Executed checks

```powershell
dotnet run --project Tools/FlightSettingsCheck/FlightSettingsCheck.csproj
dotnet build Tools/FlightUiCompile/FlightUiCompile.csproj --nologo -v minimal
```

- Settings check: **594 assertions / 110 exact physical round trips PASS**. Covers all twelve action/law/repeat combinations and eight scale levels, eleven preset values, corrupt data, preserved hidden fields, custom/preset isolation, chosen reset/immutability, version-2 active choice and preserved version-1 custom storage.
- UI compilation against the actual Unity reference assemblies: **0 warnings / 0 errors**. No Unity build was launched for this check.
- `git diff --check` on the changed UI, diagnostics and settings-check files passed.
- Source review found no overlapping hero/ready/header rectangles at 568×320 or 844×390. New slab artwork retains lane-local clipping and mirrored UVs; its physical `[8,208]×[8,100]` bounds map to the same obstacle faces as landing overlays. These are code observations, not rendered-image evidence.

## Read-only browser diagnostics

The canvas snapshot exposes `characterSelectionVisible`, `characterRects[player][hero]`, `readyRects[player]` and `trackMenuRect`. Every rectangle is `[x,y,width,height]` in **CSS pixels from the canvas top-left**; upper-lane rotation is already applied. Do not multiply by DPR. Coordinates remain available when hidden; use the visibility flag before ready-state interaction.

| Viewport | Character cards, hero 0→3 | Ready buttons, upper / lower | Menu button |
|---|---|---|---|
| 568×320 | Lower x=25/157/289/421, y=216.5; upper x=421/289/157/25, y=80.5; each 122×48 | `[189,40.5,190,32]` / `[189,272.5,190,32]` | `[141,1,274,27]` |
| 844×390 | Lower x=63/245/427/609, y=261.25; upper x=609/427/245/63, y=97.75; each 172×56 | `[327,57.75,190,32]` / `[327,325.25,190,32]` | `[141,1,550,27]` |

Menu panel: 568×320 → `[12,28,544,280]`; 844×390 → `[112,45,620,300]`. For panel `(x,y,w,h)`, let `c=(w−48)/2`, `left=x+16`, `right=x+32+c`:

- Chosen: `[left,y+60,c,28]`; old four presets: two columns of `(c−8)/2`, y+96/y+134, height 30, gap 8.
- Custom: `[left,y+172,c,28]`; older six presets link: `[left,y+208,c,24]`.
- Full tracks: `[right,y+154/182/210,c,24]`; previous-sections link: `[x+w−126,y+131,110,20]`.
- Edit values: `[right,y+106,c,24]`; apply: `[x+w−177,y+h−40,161,28]`; back: `[x+12,y+8,30,27]`.
- Previous sections page: two columns at left/right, y+94/y+132/y+170, width c, height 31.

## Pending browser QA matrix

| Case | Expected |
|---|---|
| Each of four cards, both players, center and near inside edges | Correct character and selected highlight; own ready clears; other player's state is unchanged |
| Gaps, outside card margins, space beside ready | No character/readiness change and no countdown |
| Same character selected by both; explicit two ready taps; A/L | Independent scarf/player identity; countdown starts only after both confirmations |
| Chosen → each full track; each old preset/custom → a different track | Exact physics retained, ready phase only, no automatic race start |
| Custom saved → Chosen → custom; reload after each applied choice | Last custom remains intact; applied active choice restores exactly |
| Main/editor/old-preset/old-section pages and close boundaries | No hidden legacy close hitbox, no bottom switch, no click-through |
| 568×320, 844×390, desktop, portrait, pause and racing | Version remains readable, cards remain inside their lane, portrait blocks play, upper lane remains rotated |
