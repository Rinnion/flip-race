# Track Workshop 0.1 — independent review

4 November 2026. Sol/high, bounded read-only review of backend, Web UI, protocol and launchers. Reviewer changed no source and ran no replay/performance matrix. **No remaining actionable source blocker found after the two fixes below.**

| Finding | Resolution checked in source |
| --- | --- |
| P1: `app.js` imported `model.mjs`, but the explicit server file routes omitted it, preventing UI startup. | Backend added `/model.mjs` with JavaScript MIME; Web content glob also copies it to build/publish output. |
| P2: renderer detected traps with `slow|trap`, while actual Core zone kind is `negative`; lowercase kinds also lacked localized labels. | `rewardAppearance` now maps all six actual lowercase zone kinds. Renderer consumes its label/trap/coin flags; negative uses the rose trap treatment and diamond. |

Confirmed semantics:

- Replay and UI explicitly identify one SOLO actor with lagger camera/no elimination. README explains that multiplayer bodies and future inputs can change common hidden activation; SOLO does not certify that timeline.
- Both gate bands come from `ObstacleGeometry`; finish uses all four `FinishTargetGeometry` strokes and actual stroke width. Landing marker uses its clipped linked-stone X interval and physical surface plane. Hero center/radius remain distinct from unexpanded physical bands.
- Time inputs exclude countdown. Distance inputs use independent production snapshots and bisection across speed-effect boundaries. The loop stops at terminal state; later inputs remain unreached and cannot invoke retry. Interpolation and sampled event poses are documented as display observations.
- Only reference-validation errors permit reference-free preview. Geometry still passes production TrackJson. Editor text is preserved; stale references have no usable route, and explicit cleaned download removes them. Revision checks invalidate old analysis/replay responses immediately on edits. UI also explains that importing a builtin copy requires a new track ID.
- Source and published launchers resolve paths through their own directory; backend uses `AppContext.BaseDirectory`, and the project copies Web/Catalog content. These source paths support arbitrary cwd without a neighboring checkout. Actual published-package and browser smoke remain root acceptance.

The backend owner's saved `Tools/TrackWorkshop/Tests/results.json` reports 1683 assertions and fifteen pairs; its tests cover both input axes and frozen accepted finish times. Reviewer inspected the tests/result artifact rather than rerunning them. UI owner separately reported five model tests covering classification/labels. Final browser appearance, module loading and packaging are root-owned checks.

## Final production integrity check

PASS: all **65/65** SHA256 entries in `Verification/v543-json-tracks-ui.json` → `sourceSnapshot` match current files; **0 missing/changed files**. `git diff -- Assets` and staged Assets diff are empty (both exit 0); Assets porcelain status also has **0 entries**, including untracked files.

- `Verification/FIX491-RELEASE.md`: `3071311647C1391AF4FF9DC16B1DE0EDA1B735D5B117BD078158C684E7F93CD5` — expected hash matches.
- `Tools/JsonTracksCheck/accepted-14e22ba.json`: `ABA65005AE1A4A4CD2CC6ED00013A413235AD440E47C5A02D69BC762CBB6F704` — expected hash matches.

Read-only checks only; no matrix rerun or production changes. This review file is the sole updated artifact.
