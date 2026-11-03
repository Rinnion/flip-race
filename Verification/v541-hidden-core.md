# 5.4.1: authored hidden-stone timing and warning progress

Verified 3 November 2026 against the working tree based on Unity `ec8bd56` (5.4.0-trial). Specialist: Sol/high. Scope is the core timing API and offline checks for the explicitly requested «Передумай» delivery; no geometry, UI, build, release or commits were changed here. Source: [post-5.4 feedback and chosen direction](../../flip-race/docs/research/flip-race-hidden-surprises-feedback-2026-11-03.md). Root owns [delivery STATUS](V541-RETHINK-STATUS.md) and normative feature documentation.

## API and behavior

- `Obstacle.HiddenRevealDistance` and `Obstacle.HiddenWarningDuration` are nullable doubles, immutable track input. Null preserves 440 units and the selected global warning respectively. Existing seven courses have no authored overrides.
- `HiddenObstacles.RevealDistanceFor(obstacle)` and `WarningDurationFor(game, obstacle)` expose effective values. Authored leads must be finite and positive; durations must be finite and **greater than 1/240 s**. Factory validation rejects invalid data without clamping; valid authored durations are not restricted to the five global menu choices. The duration lower bound preserves the existing observer assumption that a new warning cannot activate inside the same shared step.
- The existing analytic movement observer computes earliest reveal using each obstacle's lead. Shared deadline cuts and safe activation use that obstacle's effective duration. Candidates still reduce by exact timestamp, then stable player ID; array order and camera choice do not affect the common timeline. No new event scheduler or physics parameters were introduced.
- `HiddenObstacles.WarningProgress(game, obstacle)` is a read-only value in 0..1: hidden = 0, warning = elapsed since reveal divided by effective duration, solid = 1. Paused simulation keeps elapsed/progress unchanged. Postponement may leave progress at 1 while phase remains **Warning**; renderer must use `GetPhase` to distinguish actual solidity and keep the warning visually distinct.
- Global warning selection is never overwritten by authored timing. Retry clears reveal/progress states, retains the selected global duration and reads the same authored metadata. Existing deep snapshots already copy mutable hidden-state entries; track metadata remains shared read-only, so `RaceSnapshot.cs` needed no change.
- Gates remain solid even when valid authored hidden metadata is attached. Both collision and landing continue to use physical phase. Body-clear recognition remains bounded by 1/240 s; reveal/deadline timestamps remain analytic.

## Files changed by this specialist

- `Assets/Scripts/Core/RaceSimulation.cs`: two optional metadata fields and validation at race factories.
- `Assets/Scripts/Core/HiddenObstacles.cs`: effective timing/progress helpers, metadata validation and per-obstacle timing in the existing shared-world flow.
- `Tools/HiddenObstacleCheck/Program.cs` and `README.md`: retained 5.4 checks and targeted 5.4.1 additions.
- This report.

## Verification

`dotnet run --project Tools/HiddenObstacleCheck/HiddenObstacleCheck.csproj` — **PASS: 17 scenario groups / 1324 assertions**. The original 13 groups / 1005 assertions remain unchanged; the extension adds 319 assertions. Actual `Press`/`Advance` motion is replayed at 30/60/144 fps and requested steps of 0.317/10 s.

New checks cover:

- Multiple authored leads (180/220) and durations (0.3/0.4 s), alongside a null/default stone in one shared world; exact reveal and deadline boundaries.
- Four players with sub-tick competing crossings and reversed player/obstacle arrays; exact earliest participant and stable timestamps.
- Monotonic warning progress, pause/resume, a body's activation postponement at progress 1, safe clearance after a real jump, independent cloned forecasts and fresh retry progress.
- Selected global warning 1.2 s remaining unchanged while authored stones use other durations.
- All four custom-track factories rejecting zero, negative, nonfinite and too-short metadata; valid arbitrary durations including 0.3/0.35/0.4 s and values just above the technical step bound.
- Gates staying physically solid and all seven existing full tracks retaining null overrides/default effective timing.

`git diff --check` on owned source/test files — **PASS**, with only Git's existing CRLF normalization notice.

No new policy refusal occurred. The test command used the ordinary scoped `require_escalated` execution path for dotnet's temporary build files, as in the previous core check. No permissions were changed.

## Remaining acceptance

Root integrates the authored «Передумай» geometry and gradual warning rendering, updates current HIDDEN documentation, and runs delivery regression/build/browser/archive acceptance. This report does not establish new-course reaction windows, fairness, enjoyment, actual rendered transparency, or build correctness.
