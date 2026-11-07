# Surface run UI checks

Run `./Tools/SurfaceRunUiCheck/Run.ps1` from the Unity project. The runner first regenerates and checks production source copies through Iter49Ui, then exercises real menu, apply, cancel, mode switch, retry and Core methods with existing native environment aliases.

The checks cover both flight presets at 568×320, 800×360, 960×432 and 1280×800, each at DPR 1, 2.5 and 3. They select all 300 combinations of rate, curve, cap, air decay and collision reset and verify that each click changes only its setting. They also check the defaults, help text for all five decay choices and both reset options, pending edits, back and cancel, all five human modes with the feature on and off, forced OFF and restoration across BOT, and the settings lock during an attempt.

The layout check measures all buttons on the surface run page, including the header buttons, and checks that they are at least 44 UI units in each dimension and do not intersect each other, the title or the row captions. The retry fixture forces the finished phase to check that retry keeps the settings and clears the reserve. Browser rendering and physical touch are separate checks.
