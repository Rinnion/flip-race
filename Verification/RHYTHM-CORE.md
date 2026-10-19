# RHYTHM-01 — common offset-gate model and CPU proof

> **Historical checkpoint.** The shared Core model and compatibility results below remain valid. The offset192 candidate is historical: its short stage later exposed a real recovery RED. The release uses compact112 gates and restored two-part Flow gates, documented in [RHYTHM-GATE-FAMILY](RHYTHM-GATE-FAMILY.md) and [RHYTHM-STAGE2](RHYTHM-STAGE2.md). The statement below about unchanged TracksFull geometry describes this initial checkpoint, before the subsequent authorized Flow restoration; it is not a description of the final release. Historical figures are retained unchanged.

19 October 2026. This report covers the gate model, isolated production motion and compatibility regressions. The root-owned short stage, browser review and subsequent tracks have separate reports. No vertical physics, presets, existing TracksFull geometry or old input arrays were changed by this work.

## Production contract

`Obstacle.TopOffsetX` and `BottomOffsetX` are signed world distances relative to `Obstacle.X`. Both halves share `Width`, logical ID and penalty history. Zero offsets preserve existing straight gates.

`ObstacleGeometry.TryGetBand(obstacle,index,out band)` returns physical, unexpanded rectangles: gate index 0 is the upper part, index 1 the lower; a stone has only index 0. `ObstacleBand` has X/Y/Width/Height and Left/Right/Top/Bottom. A zero-height gate half is absent. `ObstacleGeometry.Left/Right` return the union of existing parts; absent offsets cannot enlarge that union. A fully empty gate reduces to X.

Collision broadphase uses the union; narrowphase computes an independent horizontal interval for each real part and combines it with the existing analytical vertical contact. There is no diagonal collider. The existing per-player logical hit ID suppresses a second charge from the other half, while another obstacle ID remains active. Linked confirmation uses max(authored ClearX, farthest physical Right + player radius +1e−7). Collision-first cancellation is unchanged.

## Common geometry and routes

Upper band Y0…140, lower band Y100…240, width 16 each, offset 192. For the floor approach, upper X1000…1016 and lower X1192…1208. The ceiling approach mirrors their order. The two required center-height bands are Y>154 and Y<86: no common straight middle bypass exists.

Fast takes one press from the correct real wall. Main takes a jump, then one airborne gravity change 0.20 s later. Selected first-input X values below work on either mirrored approach:

| Horizontal speed | Fast first X | Main first X | Fast full fixed-speed window | Main full window at fixed 0.20 s delay |
| --- | --- | --- | --- | --- |
|110|1074.978199|1063.546976|1254.467ms|1138.401ms|
|220|1045.956399|1023.093951|581.740ms|465.674ms|
|330|1016.934598|982.640927|357.497ms|241.432ms|

These widths are full intervals, not ± tolerances. Exact corner contact is a collision. The selected Main center is chosen from the intersection of independently perturbed input constraints. Actual production runs check −60/0/+60 ms for each input, including all nine combinations, both surfaces and six dt values. No second Fast airborne repeat is required.

The constant-speed110/330 fixtures explicitly hold a synthetic effect for geometric isolation. Separate real-ring fixtures use the ordinary 0.6 s pulse. Examples:

- Boost acquired at X900 ends at X1098 between the halves.
- Slow acquired around X1010 ends around X1076 between the halves.
- A midflight ring around X1080 starts boost or slow in the gap; the boosted pulse ends around X1278, the slow pulse around X1146.

Their nominal contacts are actual circle events, not injected effect changes. All mirrored/frame-step routes and joint ±60 ms gate-clear perturbations pass. A shifted optional-ring route can miss that ring; this proof does not claim identical reward collection at every perturbation. The wider individual search stops at 150 ms, so that output is a lower bound within the tested search, not an exact maximal window.

## Results and commands

| Scope | Result | Evidence |
| --- | --- | --- |
| Production one-obstacle CPU candidate | PASS 74 321 checks / 24 042 actual runs | [JSON](rhythm-offset-proof.json), [stdout](rhythm-offset-proof.txt) |
| Targeted Editor gate suite | PASS 4 scenarios / 2 960 assertions | Included in [engine regression](rhythm-engine-regression.txt) |
| All previous Editor suites | PASS 963 / 3554 / 865 / 1770 / 1009 / 2166 / 1598 / 1592 / 1190 | [engine regression](rhythm-engine-regression.txt) |
| Original JS differential | PASS 40 228 snapshots / 1 448 208 state fields | [engine regression](rhythm-engine-regression.txt) |
| Existing three-track author verification | PASS 48 596; both profiles, six dt, safe 36 / full 33.2 / trap 33.5 | [old-route regression](rhythm-old-routes.txt) |

Targeted tests use real presses and cover floor/ceiling, positive/negative offsets, exact and ±0.0001 corner launch boundaries, one shared gate penalty after both actual contacts, a different neighboring ID, and a collected linked ring held pending until the complete farther half has been cleared. Old straight gates retain their historical differential behavior.

```powershell
dotnet run --project Tools/OffsetGateProof/OffsetGateProof.csproj -c Release -- Verification/rhythm-offset-proof.json
dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- --offset-gates
dotnet run --project Tools/ParityRunner/ParityRunner.csproj -- Tools/engine-parity-fixtures.tsv.gz
dotnet run --project Tools/TwoModesProof/TwoModesProof.csproj -- --verify
```

These are .NET runs. Phone-size readability and ease for people who disliked a rapid double tap require the separate browser/audience check; a successful CPU schedule alone does not establish either.

## Frozen source SHA-256

| Source | SHA-256 |
| --- | --- |
| RaceSimulation.cs | `AA91B96919AE318607D8BFF180AE7A54879FA54C2C6072003F5B245A57E10C24` |
| ObstacleGeometry.cs | `BB1A5F4F79981CCDF3DABC4E9DB0511C3F9D9247B9E712ACC3FABB717653B92B` |
| EngineOffsetGateTests.cs | `FDC4466393467A0CE3DB34B9556791C8FB2E9848D342BEB55EAB26678D516EAC` |
