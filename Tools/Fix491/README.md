# FIX491: optional two-chevron finish target

Regression commands from the Unity repository root:

    dotnet run --project Tools/Fix491/Fix491.csproj
    dotnet run --project Tools/Fix491/Fix491.csproj -- --proof
    dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- --finish

The default command reproduces the exact user symptom through production Press/Advance:10actual no-action races,20real-track supported arrivals,4frozen original Mixedsafe/full routes. It failed16cases before the fix and now passes0failures. Surface setup is explicitly a state-injected geometry isolation; full races and all authored route witnesses are actual public input trajectories.

`--proof` checks5tracks×2profiles×6steps; actual no-input races and isolated wall runs; frozen ordinary bypasses; deliberate collection; all original obstacle/reward/coin/trap hashes; actual nearest-stroke contact;55units/.25movingseconds; same TIMED input without target; decisive +/-33ms and30/60fps phase quantization. Synthetic contact/timer/FIFO/recovery/pause/rematch cases are independently covered by EngineFinishTests.

`fix491-before.json` freezes baseline3214a10. Mixedsafe/full arrays are exact original arrays. For the first three tracks the old Fast final7700/7720 finish-preparation tap is intentionally omitted from the ordinary branch; ordinary obstacle and reward inputs remain. Production authored helpers are unchanged. Deliberate first-three/Cascade branches add a finishing action; Mixed retimes only its last action. No runtime reads these witnesses.

Clearly marked offline debug probes retained in this verification tool: `Grid.cs`, `Retime.cs`, and the `Search` method in `Routes.cs`. Modes `--search`, `--flow`, `--mixed-grid`, `--mixed-retime` operate on cloned tracks and report rejected/viable candidates; never part of runtime or normal regression. `--save-baseline` refuses to overwrite the frozen baseline. Early pre-freeze candidate reports are exploratory, not acceptance evidence; production proof is authoritative.

See Verification/FIX491-RELEASE.md and FIX491-STATUS.md for exact source identities, honest native attempts, browser/package checks and limits. The legacy Iter49Finish CLI forwards to this proof; historical rectangular tests/results remain accessible at3214a10 and must not be cited as current target proof.
