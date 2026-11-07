# Surface run UI checks

Run `./Tools/SurfaceRunUiCheck/Run.ps1` from the Unity project. The runner first regenerates and checks production source copies through Iter49Ui, then exercises real menu, apply, cancel, mode switch, retry and Core methods with existing native environment aliases.

The checks cover both flight presets, 568×320, 800×360 and 1280×800 at DPR 1 and 2.5. They exercise all 30 combinations of rate, curve and cap, the default configuration, pending edits and cancel, all five human modes with the feature on and off, the transition into and out of BOT, and the settings lock during an attempt. The layout check measures all 44px targets on the new page. The retry fixture forces the finished phase to check that retry keeps the settings and clears the reserve. Browser rendering and physical touch are separate checks.
