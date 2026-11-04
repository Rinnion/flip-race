# 5.4.2 — regression after adaptive hidden stepping

4 November 2026. Independent source review and reruns by Sol/high after the Core performance fix was frozen. No source, fixture baseline, historical JSON or commit was changed by this verification.

Verified `Assets/Scripts/Core/HiddenObstacles.cs` SHA256 before and after execution:

```text
07782B10C63D24690F764E663ACECAF678AFF7E62ACAAA05512CBEA2CE011C18
```

## Source review

The StepLimit diff implements the previously reviewed conservative scheme. Unrevealed ordinary stones cap the shared slice by their authored/global warning duration, so a newly observed reveal cannot also require activation inside that slice. Existing future warnings cut exactly at the earliest deadline. An overdue warning still present after Update keeps the 1/240-second body-clear bound. When all hidden stones are Solid, no hidden-specific substep is required. The observer still reports exact constant-speed segment crossings and reduces competing timestamps before shared activation; minimum-duration validation remains greater than 1/240 s. No correctness blocker found.

## Executed checks

| Runner | Result |
| --- | --- |
| `Tools/NewBotReferenceCheck/NewBotReferenceCheck.csproj` | PASS: 165 assertions / 26 complete replays; all five references, five selected warning delays on both Hidden maps/profiles, authored Rethink timing and initial-Solid fixtures |
| `Tools/HiddenTracksCheck/HiddenTracksCheck.csproj` | PASS: 52 assertions; safe, actual air/landing rewards, optional real finish, recovery, omissions and warning settings |
| `Tools/ParityRunner/ParityRunner.csproj -- Tools/engine-parity-fixtures.tsv.gz` | PASS: 40,228 JS/C# snapshots / 1,448,208 state fields, numeric tolerance <1e-8; all built-in original/V2/V3/V4/TUNE/CONTROL/FINISH/FLIGHT/MODES/offset-gate suites also passed |
| `git diff --check -- Assets/Scripts/Core/HiddenObstacles.cs` | PASS |

The two JSON-writing runners used working directory `F:/Home/Game/.scratch-v542-postfix-regression`, with absolute project paths. Fresh machine outputs are in that directory's `Verification/`; historical results in the Unity project were never overwritten. Parity used the absolute existing fixture path and did not regenerate it.

Historical hashes were identical before and after:

| File | SHA256 |
| --- | --- |
| `Verification/v54-hidden-tracks-witnesses.json` | `D201AB11E5FC57331CD21758138969972DD24D976C13074816B745F43A0E6910` |
| `Verification/v542-new-bot-reference-proof.json` | `2F78E7840B49AC23FECFB02A615FF6C2063CEF4362F2ECE089D77FB4E979D0B7` |

All requested CPU checks are complete. Core's separate Hidden/edge-case tests and root's Unity/browser performance acceptance are outside this rerun; this report does not claim those measurements.
