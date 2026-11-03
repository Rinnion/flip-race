# Rethink track check

Run from `flip-race-unity`:

```powershell
dotnet run --project Tools/RethinkTrackCheck/RethinkTrackCheck.csproj
```

The complete proof passes **125 assertions** on the frozen 5.4.1 working tree. `--quick` checks full routes, actual reward gains, the signal-relative bonus contrast, profile restriction and negative cases. `--windows` checks only representative timing windows. The default runs both and regenerates `Verification/v541-rethink-tracks-*.json`.

The scheduler calls production `RaceSimulation.Press/Advance` and `RaceSnapshot.Clone`. Motion, collisions, reward contact, support and hidden phases use production code. Responses are scanned locally in 5-unit increments; this is a bounded authoring check, not a global route optimizer. Compact outcomes retain every sampled input, while full event/input records are retained only for selected first/last successes, wrong decisions and complete routes. Phase observations include their observed pose/time and the exact production `RevealedAt`/`SolidAt`; pose observation can follow an event by up to the 5 ms scheduling step.

## What is established

- Safe: clean 36.00 s with no reward. Separate clean air, ground-bonus and marked-landing witnesses each earn and complete one real 66-unit/0.3 s effect. Optional finish gives 55 units/0.25 s.
- M1/M4: reveal is already in flight; an extra tap 0.409 s after the signal hits the actual hidden stone at all three entrance samples (−50/0/+50 ms). Abstaining is clean. Their 0.864 s windows belong to the **subsequent jump after abstaining**, not to a hidden-reaction measurement.
- M2/M5: cancellation responses are scanned after the actual signal. Minimum contiguous sampled window and last-save time are both 0.477 s across independent entrance samples. Ignoring M2's correction causes a real hidden hit.
- M3: warning follows real ground-bonus contact. The corrected jump happens during active ×1.5 speed. A 0.617 s delay after the signal is clean without the bonus and hits with it; a 0.364 s bonus response is clean. The sampled boosted-response window is 0.303 s, last save 0.485 s after the actual signal. The entrance is settled ground; previous-input jitters produce the same signal pose, rather than three airborne states.
- M6: a real marked-stone landing earns the bonus before the next warning. After returning to actual wall support, the next jump has a contiguous 0.295 s sampled window; latest save remains at least 0.711 s after reveal across the three landing-entry samples. Early successful air corrections are recorded but excluded from this supported-jump window.
- Idle and one simple repeated central oscillation fail. Omitting the M3 obstacle jump produces one hit and still finishes at 36.50 s. `IsSupported` accepts a distinct exact Chosen settings object and rejects changes to every physical/profile field, including inactive values and the legacy-linear flag.

## Limits

Independent ±50 ms entrance checks do not establish simultaneous whole-course jitter tolerance. Finite negative cases do not prove global impossibility of every bypass. For no-return, the added tap is a controlled counterfactual; actual player habit and the pleasure of changing a decision still require human playtesting. Technical timing windows do not establish phone usability, readability or fun. Build/browser/ZIP acceptance and old-course regression belong to the root integrator.
