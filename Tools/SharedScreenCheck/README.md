# Independent shared-screen checks

Run from PowerShell 7:

```powershell
pwsh -NoProfile -File Tools/SharedScreenCheck/Run.ps1
```

The script compiles the current production Core and pure `SharedFieldView` with `Add-Type`; it does not start Unity, a browser, or a server. The recorded checkpoint is [screen-00-independent.json](../../Verification/screen-00-independent.json): **5,850 assertions / 48 games / 1,008 production projections**, plus **72 synthetic camera cases**.

`Audit.cs` owns independent analytic fixtures and a clock scheduler. Gameplay transitions use only actual creation, character selection, readiness, racing inputs and advancement. Tests cover personal and simultaneous positive/negative contact, pending→success/failure, shared two-band gate hits, physical support after consuming a landing reward, personal finish and identical heroes. Six maximum time steps include large advances. Every observed state is fingerprinted before and after production pure projection calls.

`Camera()` is deliberately separate: it injects distances only to test a projection, never to claim a reachable gameplay transition. It tests current 25% / 47% camera rules, not whether those rules are comfortable. The earlier 19% / 45% checkpoint is preserved as [screen-00-independent-19-45.json](../../Verification/screen-00-independent-19-45.json). Presentation calls are not two repeated Core simulations and are not claimed to exercise Unity native drawing. Full input/layout/menu and browser acceptance follow the SCREEN gates.

The additional stage-only managed probe invokes actual compiled Presentation methods:

```powershell
dotnet run --project Tools/SharedScreenCheck/Managed/Managed.csproj
```

Recorded result: [screen-00-managed.json](../../Verification/screen-00-managed.json), **331 assertions / 56 renderer geometry calls / 20 Pointer calls**. `SharedPlayerGeometry` is called on real Core transition states. `SharedFieldRect`, `ControlAreaRect`, `ReadyScreenRect` and `Pointer` are exercised with explicitly configured synthetic arena rectangles for opposite/Half at 568 and 1280 widths. This tests the stage routing seam without inventing a second copy of its logic; it does not call native `Measure`, `Update`, `OnGUI`, input devices or GPU drawing. A 0.0001 CSS-pixel tolerance is used for Unity float geometry.

SCREEN-02 now has a separate native-boundary harness:

```powershell
pwsh -NoProfile -File Tools/SharedScreenCheck/UiInterop/Run.ps1
```

Result: **2,972 assertions / 24 matrix cases / 72 actual Apply calls**, plus 15 actual-Update state-equivalence games. See [SCREEN-02-AUDIT](../../Verification/SCREEN-02-AUDIT.md) and [JSON](../../Verification/screen-02-ui-interop.json). Both seatings/heights and two viewport sizes are tested for Main, Fast and explicit custom None. The production `Measure`, `Update`, pointer/menu/apply logic runs unchanged except native environment references: scripted Input/Time/Screen/DPR, in-memory PlayerPrefs, no-op selection texture acquisition. Generated copies live only under ignored `obj/managed-source`. All source SHA values are checked before and after the run.

Actual `Update` phase filtering is exercised: a Began moves only its fixed player, while Moved/Stationary/Ended/Canceled crossing the boundary does not trigger the other. This still does not prove native browser/device delivery, physical multitouch or pixels. The state-equivalence fixtures call production Update and shared renderer geometry; they are not two direct Core runs labelled renderer tests.

Only this directory and new SCREEN verification artifacts belong to this harness. Production Core, UI, saved releases and historical reports are not modified.
