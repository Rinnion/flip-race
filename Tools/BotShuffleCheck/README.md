# BotShuffleCheck

Run from `flip-race-unity`:

```powershell
dotnet run --project Tools/BotShuffleCheck/BotShuffleCheck.csproj -p:CheckForOverflowUnderflow=true -- Verification/bot-diversity-shuffle.json
```

This isolates `BotPlanShuffle.cs`; no catalogue, Unity, physics or UI is loaded. The JSON contains a compact seed/bag matrix, stable sequence digests, signature counts and the production source SHA256. The golden digest guards framework-independent uint/UTF16 behavior.

`new BotPlanShuffle(uint sessionSeed).Next(string key, int[][] groups)` returns immutable `BotPlanChoice` fields: `SignatureIndex`, global `PlanIndex`, `AttemptSeed` (`int`), and zero-based per-key `SequenceIndex` (`long`). Supply a stable ordinal track/profile/level key and the same ordered nonempty groups on every call. Global plan IDs must be unique and nonnegative. The first call clones the entire shape; changing the supplied content later throws instead of resetting the sequence. Calls for another key cannot advance this key. The owning game thread makes calls only when selecting an attempt. Reusing the object across menu/retry preserves session history.

Signatures receive one draw each per bag regardless of timing variant counts. Every signature owns its timing bag. Boundaries avoid immediate repetition when the relevant bag has at least two members. A singleton repeats by definition. Attempt seeds are stable diagnostic identifiers, not cryptographic randomness or a global uniqueness guarantee. No persistent progress is written.