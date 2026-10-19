# RhythmAudit — independent verification contract

20 October 2026. **Final R7 CPU verification passed, including all 3 844 actual first-hit recovery states. Catalogue/build GO was issued after all four captures completed.** Full-course browser/package evidence remains a separate release gate. See [full audit](../../Verification/RHYTHM-FULL-AUDIT.md), [stage results](../../Verification/RHYTHM-STAGE-AUDIT.md), [stage browser evidence](../../Verification/RHYTHM-STAGE2-BROWSER.md), and [R5 recovery classification](../../Verification/RHYTHM-RECOVERY-R5.md). R6 remains a complete checkpoint with six unresolved cases; it is not relabelled as accepted. Production Core, tracks and UI belong to other tasks. Historical evidence remains unchanged.

Sources read: [implementation plan](../../../flip-race/docs/flip-race-rhythm-tracks-plan.md), [rhythm appendix](../../../flip-race/docs/flip-race-rhythm-tracks-design-2026-10-19.md), and the complete [isolated prototype](../../../flip-race/docs/experiments/offset-gates/Program.cs) plus its report. Baseline preservation is recorded in [RHYTHM-STATUS.md](../../Verification/RHYTHM-STATUS.md).

## Independent oracle, not a replay-only verdict

For a floor departure, use physical center Y=226 and radius R=14. Fast has upward travel `d(t)=35t+1350t²`, with inverse `T(d)=(sqrt(35²+5400d)-35)/2700`, until real wall contact. Given an upper rectangle `[U,U+w]×[0,a]` and lower rectangle `[L,L+w]×[b,240]`, the monotone one-tap interval at constant horizontal speed v is:

```text
U+w+R-v*T(226-a-R) < launchX < L-R-v*T(226-b+R)
```

Both endpoints are strict: the existing expanded-rectangle collision regards an exact corner touch as an impact. Reflect Y and exchange the physical bands for the ceiling case; test signed X offsets separately. Additional reserve uses R+margin in contact constraints while retaining the actual starting center Y=226. The full interval divided by v is its total temporal width, not the ± tolerance.

For Main, an ordinary floor jump is `d(t)=350t−0.5*662.5*t²` until its air reversal. At a real repeat time q, retain `vUp=350−662.5*q` and accumulated displacement, then use `d(q+s)=d(q)+vUp*s+0.5*662.5*s²` until a genuine surface event. Solve each segment against independent band bounds. Do not infer Main success from Fast or describe a fixed-second-tap interval as a joint two-action window.

Author route arrays may supply comparison witnesses later, but pass criteria must also come from these inequalities, independently constructed fixtures and independently chosen perturbations. Freeze hashes are recorded before and after execution. No test will print PASS for an unimplemented scope.

## RH-01 matrix

| Area | Independent cases and expected result |
|---|---|
| Geometry helper | Each existing band's actual X interval; positive/negative offsets; absent top/bottom excluded from union Left/Right; zero offsets reproduce the baseline. Do not use one unshifted broadphase range for both halves |
| Continuous contact | Both walls, both offset signs, exact front/rear corners and inside/outside perturbations; a step crossing the entire pair, first contact at t=0, surface/turn inside gap, both player slots |
| One logical penalty | A real first-band hit, complete recovery and later physical contact with the second band yield one collision for the shared ID. Different-ID controls still produce the separate second hit. Check no zero-time loop and no immunity leaking to the other player or next gate |
| Linked clearance | Confirmation after `max(ClearX, rightmost existing band + R + epsilon)`; first-half clearance alone is insufficient. Already-pending later hit cancels; untouched reward after a historical hit retains existing semantics |
| Speed changes | Isolated constant 110/220/330 fixtures are labelled as such. Additional real 0.6-second ring pulses begin/end before, inside and exactly at band boundaries; include positive extension, slow preemption/resume, collision freeze and effect exhaustion |
| Timing | Fast one supported departure from the intended side; Main actual accepted Press sequence. Independently shift each required action by ±60 ms of actual clock, plus measured endpoints; do not divide X by220 during effects. Report marginal windows separately from any joint perturbation checks |
| Integration | Baseline straight gates and zero-height absent bands retain their exact behavior; shifted union bounds agree with display/culling/overview contracts. Browser checks both parts visible before the decision, floor/ceiling and rotated upper lane |

Use representative steps 1/30, 1/60, 1/144, 0.317 and steps large enough to cross the complete pair, while explicitly splitting only at scheduled user input. Compare actual state at the same elapsed time. Event alignment tests must not depend on the author's frame cadence.

## RH-02…04 after the stage gate

- Record each accepted gameplay Press, its actual time, support/charge and the next required region. Exclude readiness, ignored actions and cancelling pairs without meaningful consequences. Count Fast and Main separately.
- Delete one action at a time and record a concrete changed outcome. Also try shortened sequences, floor/ceiling routes, middle-hover controllers and a bounded set of regular frequencies/phases. These are counterexample searches, not a proof of global tap minimality; report simpler clean routes rather than hiding them.
- Test safe/full/trap via actual Press/Advance and physical finish. Compute group boundaries, shortest/longest input intervals and initial/final idle tails from real clocks with effects. Safe density cannot be inferred from full reward collection.
- Generate actual collision states by bounded input perturbations. After the remaining penalty plus 150 ms, branch only over legal controls and require reaching the next obstacle's trailing boundary without another impact. An empty late sample or mere survival until a short timeout is not success. State finite coverage explicitly.
- Recompute latest reward release bounds from circles, landing intervals and linked unionRight. Verify ordinary work exhausts before X7876 for all reward subsets and conservative delayed releases, with preemption/recovery preserving work. No artificial queue flush; no other event in the final44 units.
- Preserve old tracks, old artifacts and custom settings. New source/browser/physical-playtest evidence remain distinct.

## Questions resolved before assertions

1. Confirm the production band API uses offsets relative to Obstacle.X and how empty-band union bounds are represented. The planned `TopOffsetX`, `BottomOffsetX`, `TryGetBand`, `Left`, `Right` contract will be reviewed when written.
2. Define stable logical gate IDs distinct from neighboring gates; verify the common-ID one-penalty behavior is intentional for the whole passage, including after recovery.
3. Establish a common Main/Fast short-stage geometry with mandatory-action reserve before embedding it in dense full tracks. The prototype's Main window at330 is insufficient evidence.
4. Record actual visible cue/exit distances on the compact browser scene, including the downstream shifted half; source geometry alone cannot establish readability.

## Reproduction and implemented scopes

From the Unity project root, use `pwsh -NoProfile -File Tools/RhythmAudit/Run.ps1 -Scope <scope>`. No Unity build or NuGet restore is needed: `Add-Type` compiles current pure Core and these independent sources. `Capture.ps1 -Scope <scope> -OutputPath Verification/<new-name>.txt` records source SHA before/after; a failed audit never writes a PASS capture.

Implemented scopes: `stage` (own geometric oracle, six dt, individual ±60 ms, each input removal), `bypass` (72 bounded actual-play controllers), `stage-speeds` (explicit injected held-speed geometry fixtures), `geometry` (signed/absent band bounds and wall contacts), `recovery` (actual hit states and legal escape search), `parts` (both halves required in actual menu/practice gates), `authored-contacts` (isolated actual contact with every present authored band), and `two-band-penalty` (actual contact with both halves, shared/distinct IDs and next-obstacle independence).

For the mixed current stage2 use `family-stage` and `family-recovery`: independent compact/accessible schedules, own Fast quick-return0.17 s, mandatory60 ms and optional40/30 ms neighborhoods, actual first-hit recovery. `restored-flow` uses current author X-routes as witnesses with an independent real-speed scheduler, actual finish/ledger assertions and a union-bound ordinary-effect tail; `flow-recovery` samples real hits at the three restored gates. These Flow runs are not independent route discovery. `parts`, `authored-contacts` and `bypass` automatically read the current menu/practice inventory.

`modules` and `modules-compact` parse the respective immutable JSON exports, build actual mirrored tracks with distinct instance IDs, and check the entry/exit contract plus all two-module safe/full combinations. Their [separate report](../../Verification/RHYTHM-FULL-AUDIT.md) records 96 785 assertions / 2 432 games per export and the limits of composability. `courses-final` validates the runtime/export contract, 9/2/1 budget, all 2 048 ordinary-effect subsets, six actual-step routes, real-clock statistics, individual windows and single/adjacent-pair shortening. `courses-duels` checks independent safe/full/trap players in both slots; `courses-bypass` runs 216 bounded controllers. These scopes require `-CourseFile <frozen-export.json>` and never activate the production list. R5 and R6 captures remain separate from final R7.

`courses-recovery-expanded` enumerates every input in every safe/full/trap route with the original twelve time shifts. It regenerates actual prefixes, stops the nominal schedule at the first hit, waits remaining recovery plus 150 ms, then uses legal controls. The catalogue includes the original stay/one/two-input/oracle paths, three/four-tap witnesses, and actual-wall adaptive paths including a 50 ms repeat. Such a fast repeat is reported as a finite witness, never as a human comfort guarantee. Every state streams to a new timestamped `rhythm-r6-recovery-*.jsonl` or `rhythm-r7-recovery-*.jsonl`; progress is printed every 100 actual hits and after each route. Do not pipe the subprocess through a tail filter when live progress is required. `NO_FOUND_ESCAPE` remains a review request, not an impossibility claim.

Final R7 reproduction from the project root:

```powershell
pwsh -NoProfile -File Tools/RhythmAudit/Capture.ps1 -Scope courses-final -CourseFile rhythm-r7-routes.json -OutputPath Verification/new-r7-main.txt
pwsh -NoProfile -File Tools/RhythmAudit/Capture.ps1 -Scope courses-duels -CourseFile rhythm-r7-routes.json -OutputPath Verification/new-r7-duels.txt
pwsh -NoProfile -File Tools/RhythmAudit/Capture.ps1 -Scope courses-bypass -CourseFile rhythm-r7-routes.json -OutputPath Verification/new-r7-bypass.txt
pwsh -NoProfile -File Tools/RhythmAudit/Capture.ps1 -Scope courses-recovery-expanded -CourseFile rhythm-r7-routes.json -RecoverySeedFile rhythm-r6-recovery-20261019-204609.jsonl -OutputPath Verification/new-r7-recovery.txt
```

`R7RecoverySeeds.cs` reads only successful prior action proposals keyed by course/profile/route/input/shift. Every R7 prefix, current effect/support state, replayed action and complete next union are newly checked; neither old states nor old verdicts are injected. A failed seed falls back to the current finite catalogue. This reduces repeated search, not the candidate matrix. Captures include the seed JSONL, export, Core, audit source and runner SHA before/after. Use new output names to preserve historical evidence.

R7 results: main 78 917 assertions / 1 788 games; duels 9 577 / 108; bounded bypass 433 / 216; recovery 52 238 / 7 650 games, 7 632 input-error candidates, 3 844 actual first-hit states, no-found 0. Fixed-first+20 ms replay passed for 3 611 states; the others have nominal legal escapes, not a claimed fixed-input window. The separate targeted adaptive-wall timing proof is identified in the full audit. This is finite actual-play coverage, not a guarantee for arbitrary errors or a phone-comfort test.

`courses-representatives`, `courses-recovery-spacing` and `courses-recovery-replay` reproduce the isolated R5 classification and geometry-only candidate work. They read the preserved R5 export and no-found data. Candidate geometry changes are made in copied test tracks, with the entire real prefix replayed; player velocity, support, effects and charge are never injected. The final R5 candidate replay checks all 30 assigned states, every input ±20 ms separately and six step sizes through the whole next union.

Historical geometry is embedded as data in `RecoveryRed.cs`: `recovery-red` reproduces the expected RED, `recovery-map` prints necessary lower bounds, and `recovery-pitch` finds sufficient witnesses on a finite pitch grid. `compact-recovery` and `compact-proposed` use isolated candidate data without editing production tracks. Recovery controls wait through the remaining penalty plus150 ms; they may stay, use one/two taps, or settle to a real wall with one/two taps before the independently computed next passage. Success requires explicit passage beyond the next unionRight+R+1.

Current-stage scopes read the current `TracksRhythm.Practice()`. Earlier `stage`/`stage-speeds` stdout describes its recorded original eight-offset-gate SHA; those scopes intentionally require that homogeneous geometry and are not a mixed-stage test. The original 70-controller capture preceded the explicit ceiling-setup controller; the fresh stage2 bypass capture proves72. State injection is confined to the clearly labelled constant-speed fixture. Nominal, recovery, authored-band and two-band penalty tests use actual Press/Advance without injecting player state.

The [gate-shape correction](../../../flip-race/docs/flip-race-gate-shapes-correction-2026-10-19.md) and both drawings were read. The restored low family keeps its small lower threshold first and its later long upper part; same-wall Fast.One quick return, accessible opposite-wall entry and Main ordinary jump have distinct proofs. RH-01 includes the actual browser scene. Source presence/contact, browser visibility and human accessibility remain separate claims.
