# R5 recovery classification and tool-only spacing candidate

This is a recovery investigation, not a new runtime freeze or a release result. Production Core and R5 geometry remain unchanged. The original independent sample contains 207 finite-search failures; a failed catalogue alone does not prove that another collision is unavoidable.

## Reproducible result

```powershell
dotnet run --project Tools/RhythmTrapProof -c Release -- --recovery-catalogue
```

[Captured output](rhythm-recovery-catalogue.txt): **40,836 checks**, **177 actual modified route prefixes**, zero failed witnesses in the assigned classes. The prefix is generated with production `Press`/`Advance`, one actual-clock input perturbation, the real first collision, the remaining penalty, and an additional 150 ms reaction period. No player coordinates, velocity, support, collision history, or effect state are injected.

| Class | Actual prefixes | Tool-only geometry change | Continuation |
|---|---:|---|---|
| Wall at the next compact gate | 99 | None | Four accepted inputs; representative intervals are at least 150 ms. |
| Airborne after a bare compact gate | 72 | Shift the following module/suffix 30 world units later | Toggle gravity, wait for the actual opposite-wall landing, jump, repeat 50 ms later. |
| Airborne after the before-module exit | 6 | None | Four accepted inputs including actual wall transitions; some adjacent inputs are 50 ms apart nominally. |

For each prefix the first recovery input is tested at five delays: 0, 5, 10, 15 and 20 ms later than its selected baseline. For the bare-air class the baseline is exactly the end of the 150 ms reaction period. The adaptive continuation uses the actual wall contact, rather than a separate retuned clock for each delay. Each resulting witness is replayed at 30, 60 and 144 Hz, and with maximum steps 0.317, 2.5 and 100 seconds. The target is the **entire next obstacle's union right edge + player radius + 1**; the collision count must remain unchanged for both players.

These are finite sample guarantees, not a continuous-window theorem or a claim about human comfort. The short jump-to-repeat interval is stated explicitly. Recovery may collect a negative ring or another reward; all such changes are processed by the normal simulation.

The remaining 30 original cases (24 within the two-gate chain, four entering the chain, and two after its preceding stone-module case) belong to the independent auditor's separate investigation. They are not included in the 177 result.

## Why the original bare-air spacing needs attention

The reported Waves state is reproduced at X7506, center Y93.0375451963, VY−133.3238636364. The next near band begins at expanded X7631, leaving 125/220 seconds. With immediate reversal, the center reaches only Y124.2229790806 at that front, against the required Y greater than124. Delaying the first reversal while retaining the old upward acceleration consumes this margin in approximately **0.296261 ms**. The original final-gate case has a formal legal route that collects the final slow, but that tiny first-input reserve is not presented as a practical fix.

The wider search also shows why the before-exit class cannot be rejected from the initial parabola alone: a real wall landing followed by a new jump provides a different legal trajectory. The four-input witnesses cover those states without moving that boundary.

The isolated +20 candidate retains the three courses' nominal final safe/full/trap times, local final-input windows and real empty-effect wall states by the unchanged module ends, but it does not establish a practical recovery margin for all ordinary middle gates. The +25 investigation found a common adaptive branch with a 20 ms jump-repeat pair; **+30 is the selected investigation candidate**, allowing the same rule with a 50 ms pair. Runtime composition and its complete routes must be reverified before this becomes R6.

## Evidence and hashes

[Machine-readable witnesses](rhythm-recovery-catalogue-witnesses.json) include track/profile/route, perturbed input and clock shift, tool-only suffix boundary/delta, exact target, and recovery offsets for each first-input delay. SHA-256: `62C1E5416E161AB40859D0F4356E5821418DEBBBDDAF48AB60142E1C6C2609D4`.

Runtime hashes remain:

- RaceSimulation: `AA91B96919AE318607D8BFF180AE7A54879FA54C2C6072003F5B245A57E10C24`
- TracksRhythmFull: `ACBBBC9ABF4F5646B9130A96FBF6186C325D8248FCFBB23F49F3CCB8C869E14C`
- TracksRhythmNegatives: `4623F6339193B3B098AB37EA81995EB3A1935D591FFFBC0F1E1D94E5A45E97F1`

R5 browser exports and previous author/independent reports have not been rewritten. Any approved R6 composition needs distinct exports and reports.
