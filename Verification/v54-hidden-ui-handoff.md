# HID-04 presentation handoff

3 November 2026. Implemented with Sol/medium. Final frozen managed run: [v54-hidden-ui.json](v54-hidden-ui.json), timestamp `2026-11-03T17:55:12.7430778Z`. **PASS: 1488 assertions, 1704 real Pointer calls, 12 layout/profile/DPR combinations, 60 warning setting cases, 72 party map/count cases.** The runner verified all recorded Core/Presentation/Web hashes remained unchanged through execution.

## Changes

- `Assets/Scripts/Presentation/FlightSettingsMenu.cs`: seven full-track cards arranged in three columns with 44-unit heights, separate warning page with five choices, active-attempt value copied into pending selection, Apply transfers it after the final mode factory, and a guard prevents unsupported BOT Apply.
- `Assets/Scripts/Presentation/BotsPresentation.cs`: explains new-track BOT unavailability before start, retains the selected full track, and transfers warning duration through BOT retry.
- `Assets/Scripts/Presentation/CorridorArtwork.cs` and `SharedFieldArtwork.cs`: use the common Hidden/Warning/Solid phase. Hidden stones are absent; warning draws only the exact unexpanded physical band; solid uses existing artwork. Linked landing pads remain absent until solid. Gate rendering and camera projection are unchanged.
- New `HiddenObstaclePresentation.cs/.meta`: four two-unit grey-lilac lines, no fill, new assets or texture changes.
- New `Tools/HiddenUiCheck/`: compact managed runner, existing native environment seams, source-hash freeze verification and documented limitations.

`FlipRaceGame.cs` did not need changes. Startup uses Core's 0.8-second default. Core owns non-BOT retry preservation. No commits, Unity builds, browser control or artifact packaging were performed by this specialist.

## What the managed result establishes

Production menu measure/Pointer/Update/Apply methods were exercised at 568×320 and 1280×720, DPR 1/2/3, with both ready profiles. All seven distinct map targets are inside the panel, mutually separate, and clear of warning/footer targets. Actual pointer selection reaches each map. All five warning choices apply to new-map SOLO without changing physical profile settings, survive reopening, changing to an old track and SOLO retry. Both new maps retain their ID when entering BOT; unsupported Apply creates neither a new Game nor a seed. Disabled new BOT cards ignore input; explicit selection of an old map enables Apply and starts BOT. BOT retry retains the warning value. Each new map applies to human parties of two, three and four.

Retry uses explicit synthetic completed-attempt fixtures. These test the real result-button route and settings preservation; they do not prove full-track reachability. Source transformations replace only native environment dependencies, DPR and selection texture acquisition; no menu or gameplay method body is substituted. The first normal invocation hit a technical generated-file Access denied; the same bounded run succeeded through scoped `require_escalated`. No automatic approval rejection occurred.

## Remaining integration acceptance

Root owns actual Unity/WebGL compilation, browser/native pointer verification, contour and text legibility, catalogue screenshots and release/archive checks. The managed result does not prove visual rendering or physical-phone usability. Preserve VIEW/TOUCH/SOLO/BOT/REWARD/GATE and follow [Flip Race art direction](../../flip-race/docs/flip-race-art-direction.md); existing assets and Tanya's authorship remain intact. No unresolved UI code defect was found in this run.
