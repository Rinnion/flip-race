# Independent two-mode audit

19 October 2026. Test-only tools for MODES-03/04. No game runtime edits. **Independent gates PASS** on frozen Core CFA0 / Tracks717C. Final results and hashes: [MODES-AUDIT](../../Verification/MODES-AUDIT.md); earlier checkpoints: [MODES-AUDIT-PLAN](../../Verification/MODES-AUDIT-PLAN.md). Browser and package readiness are separate.

Run from the Unity project root in a fresh PowerShell process for each scope:

```powershell
.\Tools\TwoModesAudit\Run.ps1 -Scope analytics
.\Tools\TwoModesAudit\Run.ps1 -Scope after
.\Tools\TwoModesAudit\Run.ps1 -Scope late-tail
.\Tools\TwoModesAudit\Run.ps1 -Scope bound
.\Tools\TwoModesAudit\Run.ps1 -Scope stage
.\Tools\TwoModesAudit\Run.ps1 -Scope gates
.\Tools\TwoModesAudit\Run.ps1 -Scope full
.\Tools\TwoModesAudit\Run.ps1 -Scope windows
.\Tools\TwoModesAudit\Run.ps1 -Scope recovery
```

Alternatively use `dotnet run --project Tools/TwoModesAudit/TwoModesAudit.csproj -- analytics` with .NET 10. The PowerShell runner compiles current Core and this tool via Roslyn without a Unity launch. Each scope resets its own counters. Failed assertions throw; a geometry-bound printout alone does not certify the current routes.

- `analytics`: independently calculated continuous motion against production `Chosen`/`FastFlip`; exact preset fields, true supports, forbidden/accepted repeats, measurements, inactive-field independence, gate feasibility limits.
- `after`: real input trajectories and collisions, both profiles/sides and variable dt. It checks rear-face clearance, later authored ClearX, collection then hit, historical hit then untouched pickup, contact/impact order and actual finish accounting. It should be RED on the old landing-wait rule.
- `late-tail`: original D3 geometry is kept in immutable `ReviewTracksFixture.cs`; its original source SHA is in the header. Replays the former late-after defect with real hover taps and no injected flight state. It should be RED on old Core and GREEN after geometric confirmation. The pre-existing [RED evidence](../../Verification/TRACKS-LONG-TAIL-RED.txt) is never overwritten.
- `bound`: reports all 2¹¹ subsets of ordinary rewards and independent conservative/weighted release bounds for current geometry. Its assumptions require the semantic checks; it is not a substitute for route reachability, recovery, or final-target timing.
- `stage`: both profiles safe/full at six dt, each safe input shifted ±60 ms actual time, centered narrow support through natural edge fall and 150 ms reaction, and both/each chain rewards without hidden FastFlip repeats.
- `gates`: real wall passages through absent bands; retained opposite/tiny nonzero bands; both profiles/sides/six dt; actual apex tangency and expanded rear-corner contact.

`full` checks safe/full/trap, individual safe clock windows and paired final-only gain: 29 693 assertions /296 games PASS. `windows` measures each full input independently on a 5 ms real-time grid: 9 465 simulations. `recovery` creates actual first-collision states from single shifted inputs, then waits remaining recovery +150 ms and searches stay or up to two accepted taps reaching the next trailing edge: 370 sampled states PASS. This finite search does not prove arbitrary simultaneous mistakes.

To save stdout with source hashes and reject a source change during execution:

```powershell
.\Tools\TwoModesAudit\Capture.ps1 -Scope full -OutputPath Verification/modes-full-independent.txt
```

A standalone LF-only [C# snapshot](../../Verification/MODES-INDEPENDENT.cs.txt) and [source manifest](../../Verification/modes-independent-source.json) preserve the final audit code. Copy the snapshot to a temporary `.cs` file and compile it together with `Assets/Scripts/Core/*.cs` in a fresh process; invoke `TwoModesIndependentAudit.Run("full")`. Do not compile that snapshot together with the split `Tools/TwoModesAudit/*.cs`, which would define the same class twice. The snapshot was independently compiled and its analytics scope executed successfully.

The distance scheduler caps movement from the public maximum speed440 and handles real recovery separately; it does not call production collision/ring event predictors. Test comparisons allow floating-point position tolerance below 0.000002 units. Timesteps include 1/30, 1/60, 1/144, .317, 2.5 and 100 seconds. Local perturbation/recovery samples in future geometry scopes will explicitly state their finite coverage.
