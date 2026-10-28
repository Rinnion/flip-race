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
## UI501 session strip and viewport paws

Current layout assertions intentionally reflect UI501: the session strip sits
between the upper/lower selection and result rows, immediately above the
unchanged shared world during countdown/racing, and each paw uses its physical
viewport corner within SafeUiArea. The previous control-area containment and
area-relative paw inset expectations are retired by this explicit UI request.

The 216 configurations also check the strip against all cards/readiness targets,
all moved tools inside the strip, unchanged world height and Core state, nine
fresh real Update edge/corner/center inputs per player's 56px paw, simultaneous
touches plus mouse suppression, and toolbar sound/pause/background isolation.
Nine-point tests use fresh synthetic racing Core states to isolate each edge;
the existing readiness/countdown/multitouch streams still execute real UI input.

Save UI501 output separately:

    powershell -NoProfile -File Tools/PartyUi/Run.ps1 > Verification/ui501-party-ui.json

Native pixels and device timing remain separate visual checks.
The UI501 checks also measure the production countdown helpers and require the
full 60px glyph box above the session strip for upper players or below the world
for lower players. Pause title (30px), subtitle (28px), resume (33px), and any
drawn icon must fit the arena without overlapping the session strip or each
other. These bounds checks complement the native screenshot review.