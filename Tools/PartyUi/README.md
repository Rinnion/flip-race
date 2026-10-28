# Party UI integration check

From the Unity repository run:

    powershell -NoProfile -File Tools/PartyUi/Run.ps1 > Verification/v5-party-ui.json

Uses real production menu navigation, Pointer, Measure, Update, PawControls and Core.
Only native environment dependencies (Screen/Input/Time/PlayerPrefs), DPR lookup,
and selection texture acquisition use the existing managed interop shims.
The runner hashes every Core/Presentation/Web source and rejects a run whose
production sources change during execution. Generated alias copies stay in obj.

216 cases cover four viewports (568x320, 800x360, 960x432, 1280x720), DPR 1/2.5/3,
asymmetric safe areas, 2/3/4 participants, both cameras and both flight presets
plus persisted custom settings. Tests exercise six >=44px cards and independent
>=44px readiness targets, physical corner/hand mapping, fixed-window uniform
projection and resize, personal reward availability, simultaneous touches with
compatibility mouse suppression, isolated edges, cross-zone holds, quick repeats,
pause/resume, terminal action/feedback gating and rematch state. Eighteen
additional menu cases return both party cameras/counts to all legacy views.

Native rendering, physical devices and event timing/chronology are separate checks.
Availability and terminal-state fixtures are explicitly synthetic. Input-only
witnesses restart with the same configuration between isolated input streams.