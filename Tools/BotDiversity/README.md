# BOT-DIVERSITY outcome generator

Run from flip-race-unity: `dotnet run --project Tools/BotDiversity/OutcomeGenerator.csproj`.

The offline tool emits `Verification/bot-diversity-outcomes.json` and `.txt`, checkpointing completed libraries. It chooses12 actual event signatures in each of40 non-Master libraries. IDs are SHA256 of canonical successful/missed/hit goal IDs; timing variants are a separate integration stage. Every selected basis is fully verified through ordinary Press/Advance at1/120 under Lagger and Constant. No player state, physics, catalog or production data is changed.

Search is directed and capped at6000 distinct tapes per track/profile pair. It starts with maximal-goal single-hit obstacle tests, then hybrid reward sections and legal recovery mutations. Selection checks positive/hit masks, thirds, nonconsecutive sets, early-miss/late-success and the inverse, mixed rewards+hits, and actual Constant visibility for the two junior Cascade levels. Compact unused alternatives are retained for integration tradeoffs. Failure is reported as BOUNDED_SHORTFALL, never hidden by a fallback or seed filter.

Counters distinguish Probe.Run calls from ordinary timed replay games. Timed games include one whole selected action-tape simulation plus partial replays to each real event timestamp to obtain its distance. Event order uses LastSuccess/LastHit/LastFinishBoost times; first-contact times unavailable from Core are recorded as observed intervals. These selected timed-mask/time checks do not replace the separate full-state observer replay proof.

The report records all current Core source hashes and refuses a checkpoint if those files change. Keep existing Core files stable throughout generation; regenerate timing/selection evidence after any intentional change affecting their behavior. This tool does not build WebGL or modify historical5.2.0/spike reports.
