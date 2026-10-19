# RHYTHM-01 browser checkpoint — 4.6.0-stage2

19 October 2026. The restored old gates and the short course passed the browser checkpoint. This does not complete RHYTHM-02–05 or establish human comfort.

## Build and input method

Unity 6000.0.64f1, `Builds/WebGL-v4.6.0-stage2`, five files / **8,683,111 bytes**. `Logs/rhythm-stage2-build-authorized.log` records `FLIP_BUILD_OK` and exit 0. Earlier sandboxed licensing initialization and the first locked-project retry did not produce the validated build.

The build contains `TracksRhythm.cs` SHA256 `8C77E83043D3A7CC3496B0206FD8D46DCFC9B0CD670F5527906F9C9890EF0E1C`, restored `TracksFull.cs` SHA256 `901255021DD6069E43734E80D16B88E5DC82824CB973239A451D8999317FA576`, and the outlined finish triangle. The three new full tracks are not in this checkpoint build.

Tests used the actual WebGL page at loopback port 4199, viewport **568×320**, native simultaneous `A`/`L` key edges and the read-only published canvas diagnostics. No Unity state injection or runtime method calls were used. Input scheduling predicted from the last published distance, speed and timestamp with a 20 ms lead. Both independent player states are retained in the raw logs.

## Short course

| Profile | Variant | Sent gameplay key edges | Both players |
|---|---|---:|---|
| Main | Normal | 18 | 20.909090909 s, 0 hits, 0 rewards |
| Main | Same-wall return | 20 | 20.909090909 s, 0 hits, 0 rewards |
| Fast.One | Normal | 10 | 20.909090909 s, 0 hits, 0 rewards |
| Fast.One | Same-wall return | 12 | 20.909090909 s, 0 hits, 0 rewards |

The Fast return was also repeated while taking live screenshots: again both players finished cleanly. At the low threshold the repeat is spent (`airFlipAvailable=false`); after the real floor landing it is restored. The normal passes were uninterrupted. The first return run included two deliberate screenshot pauses; the later live-capture run did not pause. Pause-overlay screenshots are retained as diagnostic history, not used as visual evidence of the obstacle.

- `rhythm-stage2-browser.json`: four complete routes and actual input snapshots.
- `rhythm-stage2-browser-visual.json`: uninterrupted live-capture repeat.
- `rhythm-stage2-low-live-approach-568.png`: the small physical threshold is visible before the taller, later upper part.
- `rhythm-stage2-low-live-return-568.png`: both characters above the threshold during the same-wall return.
- `rhythm-stage2-*-normal-568.png` / `*-return-568.png`: finish screens.

The compact offset family, straight gate and restored low form are all exercised on both rendered lanes. The persistent build version remains beside the title during readiness, menus, racing, pauses and finish.

## Restored Flow and finish target

Fast full Flow: **25 inputs, 9 ordinary rewards + 1 finish reward, 0 negative rewards, 0 hits, 33.2 s** for both players. This exercises all three restored Flow gates and a real finish hit.

Main Flow: all full-route inputs except the final aiming jump were sent deliberately. Both players collected **9 ordinary rewards**, passed the restored gates without hits and finished in **33.3 s with 0 finish rewards**. This is a deliberate finish miss, not a failed 33.2-s claim.

`rhythm-stage2-release-browser.json` contains both runs. Live screenshots `rhythm-stage2-finish-approach-568.png`, `-target-568.png`, `-hit-568.png`, `-miss-568.png` and `-passed-568.png` show approach, the triangle tip at the target, hit feedback and passing outside the actual window. The triangle is distinct from the finish tape, stationary, mirrored correctly between lanes, and drawn behind the character. The enlarged sign does not enlarge the contact window: the wall-running Main miss receives no finish reward.

Browser console error inspection returned an empty list after the short-course runs. CPU coverage and its limits remain in `RHYTHM-STAGE2.md`, `RHYTHM-STAGE-AUDIT.md` and `RHYTHM-GATE-FAMILY.md`. In particular, held-speed isolation is not a claim that the same straight-gate input works at every speed. The previous offset-192 recovery RED and original stage artifacts remain unchanged. A short human playtest is still needed to judge readability and the optional return's difficulty.
