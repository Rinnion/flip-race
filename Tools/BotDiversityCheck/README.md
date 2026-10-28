# BOT-DIVERSITY actual runtime replay

Run from flip-race-unity after generating the final PASS catalogue with Tools/BotDiversity/ExportCatalog.csproj. The checker links real Core sources and SharedProbe; it does not use Unity/native input or replace physics.

```
dotnet build Tools/BotDiversityCheck/BotDiversityCheck.csproj
dotnet Tools/BotDiversityCheck/bin/Debug/net8.0/BotDiversityCheck.dll --smoke
dotnet Tools/BotDiversityCheck/bin/Debug/net8.0/BotDiversityCheck.dll --batch=0 --parts=3
dotnet Tools/BotDiversityCheck/bin/Debug/net8.0/BotDiversityCheck.dll --batch=1 --parts=3
dotnet Tools/BotDiversityCheck/bin/Debug/net8.0/BotDiversityCheck.dll --batch=2 --parts=3
pwsh -NoProfile -File Verification/bot-diversity-runtime-summary.ps1
```

Build once before running the independent batch processes concurrently. Keep Core, Presentation and Web files stable during release verification. The source-freeze report records exact release-machine bytes; source line-ending changes require a fresh local freeze/run rather than pretending historical hashes still match. Immutable calibration/evidence JSON is excluded from Git newline conversion because the generated catalogue and evidence reports hash its exact bytes.

The full schedule is all1799plans ×2cameras ×3frame schedules plus20 deterministic signature-first choices ×50libraries ×2cameras =12794 cases. The batch assignment is canonical serial modulo parts; no failed choice is removed. Human idle/success/error sequences, invalid/paused deltas and personal/world terminal freezing are included. Explicit pause phase fixtures are applied to both games and labeled.

Each actual BotRaceController observer Advance(dt)/Press is replayed immediately on an ordinary CreateParty participant. Every public personal scalar, nested record, ordered queue and set is compared after the same calls, with exact equality. Reference terminal goals, collisions, time/distance and input acceptance/timestamps are also checked with stated numeric tolerances. One representative full tape is retained; all other state comparisons stream without enormous snapshot logs.

The strict summary checks all three PASS reports, sources, exact unique coverage, paired session choices, replay/error bounds and actual signature-uniform level means. Missing/stale/failed/filtered parts cannot produce partial PASS. This proves ordinary-input execution and deterministic selection, not native UI rendering, continuous feasibility between sampled window points, or that the game feels enjoyable. Native browser evidence and producer window proofs are separate.