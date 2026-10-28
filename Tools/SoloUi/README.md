# SOLO managed UI check

Run from the Unity repository:

```powershell
& Tools/SoloUi/Run.ps1 > Verification/solo-ui.json
```

The runner compiles the real production Core, presentation and Web sources. As in PartyUi, generated copies of FlipRaceGame.cs and FlightSettingsMenu.cs redirect native Screen, Input, Time and PlayerPrefs to the shared environment, and native DPR/selection texture acquisition to explicit shims. No gameplay, menu, Update, Pointer or layout method body is replaced. Source hashes before and after the run identify the tested source and reject changes during a run. Requires .NET 10 and the Unity managed assemblies referenced by SoloUi.csproj.

Coverage: actual menu entry, six hero choices, start/countdown/Update, both solo cameras, manual settings and both presets; safe-area layout at 568x320, 800x360, 960x432 and 1280x800 with DPR 1/2.5/3; toolbar and world separation in each phase; one bottom-right 56x56 paw and all nine edge/interior sample points; Began/mouse duplicate, hold/move/end, mouse-only, A and inactive L/S/K; background and toolbar input; pause/resume; resize state preservation; specific finished/eliminated title and result actions; actual retry resets temporary state while retaining track/hero/settings/camera; actual track menu; complete solo → company 2/3/4 → split/ghost/shared opposite/shared adjacent → solo transitions. Settings preferences are seeded, snapshotted and reused by a new initialized UI; result/retry cannot add stored progress or overwrite existing controls/sound.

Terminal states and isolated racing input states are explicit synthetic Core fixtures. Those fixtures test UI dispatch, gating and reset, not terminal reachability, deterministic physics parity or event chronology. Core SoloCheck covers those claims. Reflection invokes existing private lifecycle/input methods and initializes an unconstructed MonoBehaviour; it does not replace production methods. Preferences dictionaries are read only to inventory stored keys/values. No native rendering, browser, visual typography or measured physical multitouch claim is made. Browser verification remains required by SOLO-04/08.

Contract: [SOLO delivery](../../../flip-race/docs/flip-race-v5-solo-delivery-2026-10-28.md). Delivery [STATUS](../../Verification/V5-SOLO-STATUS.md). Preserves VIEW, TOUCH, CONTROL, REWARD, FINISH and GATE via production reuse; this test changes no artwork or playable geometry.
