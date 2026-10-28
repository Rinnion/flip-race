# Diversity catalogue export

Run from `flip-race-unity`. Coordinate source freeze before regenerating the production catalogue: other workers compile against its current API.

```powershell
# Read/validate only; works without compiling Core.
dotnet run --project Tools/BotDiversity/ExportCatalog.csproj -p:GenerateOnly=true -- --validate-source

# Only after final PASS, complete50 libraries and integration-owner freeze.
dotnet run --project Tools/BotDiversity/ExportCatalog.csproj -p:GenerateOnly=true -- --generate

# Recompile generated Core and compare every exported value to the final source.
dotnet run --project Tools/BotDiversity/ExportCatalog.csproj
```

The only generation input is `Verification/bot-diversity-plans.json`. Partial checkpoints are rejected before writes. Validation requires schema1, PASS, stable source, no blockers, exact5tracks×2profiles×5levels, consistent catalogues, valid real-signature partitions, distinct plan IDs/tapes and complete local signature groups. It checks declared20 whole-route timing proofs (Master/Confident) and30 explicit references, plus per-plan Lagger outcomes/ranges and Master Constant100 finish. This validates exported evidence integrity; it does not replace running the physics calibration or proving source observations independently.

`BotPlanCatalog.SignatureGroups` returns deep-cloned local flat indices. Feed them to the session shuffle, then pass `choice.PlanIndex` directly to `SelectAt(track,flight,difficulty,index,choice.AttemptSeed)`. `SelectAt` never derives an index from attemptSeed. `Select(...,seed)` remains deterministic signed modulo for reproducible tools. `BotPlan.SignatureId` identifies the chosen outcome signature. Arrays stay private and returned arrays are copies.

`TotalPlans` and authoritative `SourceReportSha256` are generated from the complete report. Historical First/Complex report constants remain for old tool compilation and do not describe current selection provenance. Existing catalogue `.meta` is preserved. Generated C# stores roundtrip double literals in separate per-library methods. The full verifier compares all IEEE754 values, IDs, signatures, groups, catalogue hashes, source hash, deep copies, exact-profile gating, signed compatibility seeds and direct-index seeds.