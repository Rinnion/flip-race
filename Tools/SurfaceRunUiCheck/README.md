# Surface run UI checks

Run `./Tools/SurfaceRunUiCheck/Run.ps1` from the Unity project. The runner first regenerates and checks production source copies through Iter49Ui, then exercises real menu, apply, cancel, mode switch, retry and Core methods with existing native environment aliases.

Coverage: both flight presets, 568×320 / 800×360 / 1280×800, DPR 1 / 2.5; human default ON, pending versus active settings, cancel, comparison OFF across all human modes, BOT forced OFF while preserving the human preference, returning from BOT, retry reserve reset, active attempt settings lock, and 44px toggle bounds. The retry fixture forces the finished phase. Visual rendering and physical touchscreen testing remain separate.
