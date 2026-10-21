# MixedAudit — independent MIX stage verification

21 October 2026. The narrow stage audit passed on `TracksMixedStage.cs` SHA `C808CD0898D5180597DCCD8CB20A7C2F893FAD037B0043A0018061114AFAADD0`. See [results and limits](../../Verification/MIX-STAGE-AUDIT.md). Core physics, course geometry, UI, historical captures and the existing large route search are not modified here.

`Adapter.cs` accepts a production `Track`, immutable `FlightSettings` and a clock schedule. It starts through two real readiness inputs and countdown, then calls actual `Press`/`Advance`. It records acceptance, both velocities, support/side/obstacle, charge, recovery and effects at each action. It never injects player state. Author schedules can later supply comparison witnesses; they cannot alone establish independent clearance, windows or recovery.

The preparation command compiles the pure Core and adapter only:

```powershell
pwsh -NoProfile -File Tools/MixedAudit/Run.ps1 -Scope prepare
```

It prints `PREPARED`, never a test count or PASS. It does not execute a course, export a gameplay result, build Unity or open a browser.

## Executed scopes

```powershell
pwsh -NoProfile -File Tools/MixedAudit/Run.ps1 -Scope stage
pwsh -NoProfile -File Tools/MixedAudit/Run.ps1 -Scope pulses
pwsh -NoProfile -File Tools/MixedAudit/Run.ps1 -Scope held
pwsh -NoProfile -File Tools/MixedAudit/Run.ps1 -Scope recovery
pwsh -NoProfile -File Tools/MixedAudit/Run.ps1 -Scope joint
```

`stage`: 10,673 checks / 456 games; `pulses`: 744 / 48; `held`: 1,032 / 72; `recovery`: 1,110 / 16 actual first-hit witnesses from 82 bounded candidates. `joint` is a diagnostic, not an acceptance claim for the whole ±60 ms Cartesian box: two mirrored opposite-shift cases hit, while all individual shifts retain the intended safe maneuver.

`StageAudit.cs` uses authored full-stage inputs as comparison witnesses and independently checks explicit physical bands, complete unions, semantic supports, actual One consumption and finish accounting. Its additional small-flight oracle derives timing from the parabola without reading route arrays. `Fixtures.cs` derives separate actual pulse schedules; only the explicitly labelled held-speed scope supplies an ActiveEffect. `Recovery.cs` retains actual prefix state after an ordinary input mistake, waits the remaining penalty plus 150 ms and uses actual-wall-adaptive inputs. No vertical state/support/charge is injected.

## Narrow scope boundaries

- Both Main (Jump350/g662.5/Unlimited) and Fast (Flip/Gravity/One/2700/35), floor and actual mirrored ceiling starts.
- Both stage orders: small→large→low and small→low→large. Physical bands and complete union bounds, not just a module's nominal endpoint.
- Main natural jump over 64×48 and a meaningfully different response to 200×92. Fast same-wall return through one real accepted repeat and actual support/recharge before the next action requiring a charge.
- Clearly labelled held-speed geometry fixtures at 110/220/330, separate from real 0.6-second pulses beginning/ending within the sequence. Recompute timing from the active speed; do not present a 220 schedule as a 330 test.
- Individual mandatory ±60 ms, and separate joint shifts of departure/return where relevant. Opposite ±60 ms shifts alter the inter-input interval by 120 ms; independent one-action windows do not prove that box.
- Real impact prefixes followed by remaining recovery plus 150 ms, and actual clearance of the next full obstacle union. A finite catalogue failing to find escape is a finding for review, not an automatic impossibility theorem.
- Event boundaries and several integration steps, including large dt. Observed `flipElapsed`, diagnostic publication cadence and any timing percentile are not FPS or a phone-performance measurement.

The full 7920-unit course, its catalogue and final delivery remain separate gates. Baseline and catalogue-index contract are in [MIX-STATUS.md](../../Verification/MIX-STATUS.md).

## Full-course replay

`FullAudit.cs` passed **409,646 checks / 84 actual games** on frozen source `4658023A…`. Final `01C67639…` differs only by one comment; reversing that comment exactly reproduces the tested SHA. See [full report](../../Verification/MIX-FULL-AUDIT.md) and [all game results](../../Verification/mixed-full-independent.json). The API is `TracksMixed.Get()`, `SafeRoute`, `FullRoute` and `NegativeRoute(FlightSettings, int mask=3)`.

```powershell
pwsh -NoProfile -File Tools/MixedAudit/Run.ps1 -Scope full-prepare
pwsh -NoProfile -File Tools/MixedAudit/Run.ps1 -Scope full
```

The 84 games cover Main/Fast, six dt values and three symmetric routes plus safe/full and full/trap in both player-slot assignments. Every player has an independent X event schedule; the common time step is bounded by both players' next inputs and a conservative maximum speed. It does not reuse the author's replay scheduler or discover routes. Safe finished36.0, full33.2 and trap mask3 finished33.8 for both profiles, with zero hits.

Acceptance checks real finish distance and observed crossing time, accepted inputs, measured ordinary/negative/finish counts and winner. Trap timing derives from its actual counts rather than assuming nine ordinary rewards. Every player must have no ordinary active/queued effect or unconfirmed linked reward just before X7876. This route checkpoint is separate from root's all-subsets tail theorem. Broad recovery search is outside this scope.
