# Independent BOT UI integration check

From flip-race-unity run `pwsh -NoProfile -File Tools/BotUi/Run.ps1 > Verification/bots-ui.json`.

The harness exercises production Update, Pointer, FlightPointer, layout, BotRaceController and Core. It covers 1200 track/profile/level/camera/viewport/DPR setup combinations, 48 detailed input configurations, and100 real races to both terminal outcomes using30/60/irregular cadence. No player state or terminal result is injected: fresh races use the production ResetBotRace/start flow. Actual result Choose track/retry, nonzero-cursor pause/resize, manual gating and SOLO/company/legacy transitions are checked. Preferences are compared around real results/retries.

Only native environment dependencies are aliased: Screen, Input, Clock, PlayerPrefs, DPR and native selection texture acquisition. Production method bodies are copied unchanged; no gameplay method is replaced. Full Core/Presentation/Web source hashes are captured before execution and checked afterward. The report is invalidated if any source changes during the run.

This is managed behavioral/rectangle evidence. It does not render OnGUI, validate colors/text overlap, replace native browser verification, or establish physical-device multitouch behavior. Runtime observer tape replay is a separate check.
