# Hidden tracks managed menu checks

From the Unity project root, after coordinating a freeze of `Assets/Scripts/Core`, `Presentation`, and `Web`:

```powershell
pwsh -NoProfile -File Tools/HiddenUiCheck/Run.ps1
```

`-BuildOnly` prepares the native environment seams and compiles without running the assertions. The full run rejects a result if a production source hash changes during execution. Its JSON contains the source hashes and timestamp; save that output as the delivery evidence after a successful frozen run.

The check compiles production presentation, Core and Web sources against Unity's managed assemblies. It reuses `SharedScreenCheck/UiInterop/Environment.cs` for clock, display, input, preferences and selection textures. The three generated source copies replace only those native dependencies, the DPR call and the native selection texture acquisition. Menu layout, Pointer routing, Update, Apply and Retry method bodies remain production code.

The matrix covers 568×320 and 1280×720 logical viewports at DPR 1/2/3 with both ready profiles. The catalogue now contains eight unique tracks. Prior selection and layout cases for the original seven remain: seven distinct track targets without overlap, five warning values, Apply after final game creation, reopen/track-change persistence, SOLO result retry, explicit old-track BOT choice/retry, and both 5.4 maps in human parties of 2/3/4. For 5.4.2, the former hidden-track BOT rejection cases intentionally expect a successful exact-map Apply and one fresh seed; selection between the two hidden BOT cards also succeeds. Historical JSON results are preserved. The profile-specific `rethink` track is checked separately by `Tools/RethinkUiCheck`, and the complete new BOT map/profile/difficulty/delay matrix by `Tools/NewTrackBotUiCheck`.

Retry tests explicitly use synthetic terminal fixtures. They establish result-button routing and settings preservation, not full-track reachability. New-track gameplay and clean routes belong to the Core/track checks. Managed rectangles do not establish font fit, contour rendering, actual browser events or physical phone usability; check these in the actual WebGL build. Graphics acceptance follows [Flip Race art direction](../../../flip-race/docs/flip-race-art-direction.md).
