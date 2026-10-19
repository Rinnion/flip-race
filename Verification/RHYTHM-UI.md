# RHYTHM-01 presentation checkpoint

19 October 2026. Runtime changes limited to `CorridorArtwork.cs`, `FlipRaceGame.cs` and `FlightSettingsMenu.cs`. Core, geometry, build/version settings and historical 4.5.1 artifacts were not changed by this task. Browser verification is performed separately by the parent task.

## Physical offset-gate rendering

The corridor now draws each nonempty gate half from `ObstacleGeometry.TryGetBand`: its actual X, Y, width and height. Visibility uses the union `Left/Right`, so the second half remains visible after the first half leaves the viewport. Both halves use the same logical hit state and fade together after the one penalised collision.

The two parts are not connected by a diagonal line. A short vertical arrow before the first part indicates the authored entry direction: upper part first means floor-to-ceiling, lower part first means ceiling-to-floor. This convention is appropriate to the authored RHYTHM passages; it is not a proof of every conceivable custom pair of rectangles. The stage's small text hint also names the intended entry surface and then reminds the player that the second part is still ahead.

Rendering follows the existing lane-local matrices: upper lane outer rotation of 180 degrees, then the corridor's X reflection. Physical world X therefore keeps the same screen direction for both players, while the ceiling/floor presentation rotates with the upper player. No new GUI clipping groups were introduced. The legacy precise practice marker is skipped for offset passages, since its old single-plane timing formula would be misleading. Aligned straight-gate markers remain unchanged.

The divider stays a progress display; no new runtime minimap was added. The [physical band diagram](rhythm-offset-bands.svg) instead uses the helper-exported rectangles from [rhythm-ui-layout.json](rhythm-ui-layout.json). It is a geometry diagram, not a browser screenshot or collision envelope. Radius expansion belongs to Core and is not rendered as a solid obstacle.

## Track selection

The catalogue uses `RaceTracks.All/Get/IsFull`. It currently contains the existing three full tracks and automatically accommodates the three new entries after their separate geometry gates pass. No empty or invented track buttons are displayed.

The main menu preserves both presets and custom, with a large card showing the pending track and opening a dedicated selection page. That page has two columns and three rows of 44-pixel-high track buttons. Selection changes only the pending track; the separate `К готовности` action keeps the selected physics and both characters, creates a ready game, and does not start it. Returning between pages does not reset the custom set.

The previous-section page now has seven 34-pixel-high buttons in two columns, including `Проба смещённых ворот`. The old repeated footer note was removed to leave room for its fourth row. The settings editor, native copy contract, release barrier and page-specific pointer routing are unchanged. Inactive pages have no active button handlers.

At **568×320**:

- Main track selector: `[292,182,248,64]`.
- Previous-section link: `[430,159,110,20]`.
- Full-track columns start at X28 and X292; rows start at Y105, Y154 and Y203; each button is 248×44.
- Stage entry, seventh previous-section item: `[28,224,248,34]`.
- Apply: `[379,268,161,28]`.
- The panel begins at Y28, below the persistent version badge.

## Executed checks and limits

```powershell
dotnet build Tools/FlightUiCompile/FlightUiCompile.csproj --nologo -v minimal
dotnet run --project Tools/RhythmUiCheck/RhythmUiCheck.csproj
```

Compilation against Unity reference assemblies passed with **0 warnings / 0 errors**. The isolated UI check passed **244 assertions**. It invokes the actual production menu rectangle methods through reflection without a native MonoBehaviour lifecycle or GUI, checking the six full-track slots and seven previous-section slots at 568×320, 844×390 and 1280×720. It verifies minimum button height, panel/viewport containment and absence of overlap between cards, Back and Apply. It also exports actual stage bands and checks the direction cue against their order.

This does not simulate touch input, font rendering, Unity clipping or mobile performance. R01 browser QA still needs both entry directions on both halves, visibility of the farther part at viewport edges, stage selection on 568×320, preserved preset/custom/characters, and modal input barriers. A human playtest remains separate from the automated route proof.
