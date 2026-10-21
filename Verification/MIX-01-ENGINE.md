# MIX-01: short-stage Core evidence

21 October 2026. This report closes the short-stage gate only. Full mixed-course authoring starts after the parent-issued MIX-01 browser/CPU GO; no full-course completion is claimed here.

The frozen `TracksMixedStage.cs` provides four unique IDs: `mixed-stage`, `mixed-stage-reordered`, and their `-mirrored` variants. Both profiles retain their released physics: Main Jump350/g662.5/unlimited, Fast Flip35/g2700/one air repeat. Track length is 2700. A 64×48 small stone, 200×92 large stone and a genuine low gate appear in two orders. The low gate has width16, lower height16 first, upper height114 offset80 later. It is not a compact112 gate.

The full route earns an exact small-face landing and an independent linked reward before the large stone. Safe and quick-return finish at 12.272727272727273 seconds with zero rewards; full finishes at 11.672727272727274 with two fully served rewards. All these witnesses have zero collisions for both players and exact ties.

## Reproducible author checks

Run from `F:/Home/Game/flip-race-unity`:

| Command | Final result | Captured output |
|---|---:|---|
| `dotnet run --project Tools/MixedStageProof -c Release -- --verify` | 15,513 checks / 444 games | `mixed-stage-author.txt` |
| `dotnet run --project Tools/MixedStageProof -c Release -- --services` | 12,987 checks / 444 games | `mixed-stage-speed-services.txt` |
| `dotnet run --project Tools/MixedStageProof -c Release -- --pulses` | 3,165 checks / 84 games | `mixed-stage-real-pulses.txt` |

The ordinary stage scope covers both profiles/orders/mirrors, six frame sizes (30/60/144 Hz, .317, 2.5, 100 seconds), and individually shifted inputs: safe ±60 ms, full/quick-return ±25 ms. Semantic assertions require Main's single natural small jump; Fast's real airborne repeat with continuous velocity and consumed charge; subsequent original-wall support/recharge; opposite-wall support for the large stone; exact small-stone support for the landing reward; and complete low-gate passage followed by a real wall. The final count includes additional exact-position observation marks; no runtime source changed to obtain it.

The held-speed scope is explicitly an isolation fixture. A long active effect holds horizontal speed at 110/220/330, while real Core handles vertical input/support and every collision. All mandatory service inputs also pass individual ±60 ms. At 110, the above-48 interval of Main's natural jump spans only about 80.59 horizontal units, less than the expanded 92-unit small rock; the service route therefore uses a real opposite-wall bypass. It does not claim the same motion works at every speed.

The real-pulse scope injects no dynamic state. A genuine additional ring precedes the same reordered obstacles: instant at Main 1140/Y120 or Fast 1180/Y226 before low, instant 2000/Y226 before large, or negative 2070/Y226 before large; mirrors invert Y. It confirms speed 330 at the low threshold, speed 330/110 at the large launch, actual same-wall landing/recharge before reuse, six frame sizes, and fully served actual finish accounting. The slow Main fixture additionally collects the existing large linked reward. These local ring-prefix fixtures are distinct from the browser stage: its early landing boost has already expired before low.

## Independent and browser evidence

Delivery's independent scopes passed: stage 10,673/456 games; real small-ring pulses 744/48; held small-speed service 1,032/72; recovery 1,110/16 actual first-hit states from 82 candidates. Recovery uses actual failed prefixes, remaining penalty plus 150 ms reaction, lawful inputs and clearance of the entire next obstacle union. Captures are `mixed-audit-stage.txt`, `mixed-audit-pulses.txt`, `mixed-audit-held.txt`, and `mixed-audit-recovery.txt`; those are independent evidence, not included in the author counts above.

Parent completed six native-key browser races at 1280×720 and 568×320 with both players clean, zero collisions and the expected times/rewards. See [MIX-STAGE-BROWSER.md](MIX-STAGE-BROWSER.md) and `mixed-stage-browser.json`. The author .NET runner did not launch Unity; the parent build/browser report records that part of validation.

## Explicit limits

Fast's nominal small safe pair 530/596 has a real airborne repeat after .3 seconds. Its unconstrained apex exceeds the corridor, so it inertially touches the opposite wall without landing/recharge, then returns to the original wall. This is a one-repeat same-origin return, not a low hop.

Individual ±60 ms is not a joint Cartesian tolerance. Independent diagnostics retain the real counterexamples: departure+60/repeat−60 causes a hit in both mirrors; departure−60/repeat+60 reaches the opposite supported wall before the second input. Full/quick-return ±25 ms is likewise an individual-input claim. Native browser success does not establish human ease or joint timing immunity.

## Exports and frozen source

`mixed-stage-routes.json` is the definitive eight-row browser export: ID, profile/preset/settings, safe/full/quick-return distance arrays, actual elapsed input times, expected finish, obstacles and zones. `mixed-stage-draft.json` remains the detailed 24-row trace with support/velocity observations. Service and real-pulse fixtures have their own clearly labelled JSON exports.

| File | SHA256 |
|---|---|
| `Assets/Scripts/Core/TracksMixedStage.cs` | `C808CD0898D5180597DCCD8CB20A7C2F893FAD037B0043A0018061114AFAADD0` |
| `Assets/Scripts/Core/RaceSimulation.cs` | `AA91B96919AE318607D8BFF180AE7A54879FA54C2C6072003F5B245A57E10C24` |
| `Assets/Scripts/Core/FlightSettings.cs` | `8499B6DC0D17A86A9CE02CE3F56FA4BD78D4F66CA1CB61E85B052C6BE5C9FB5E` |
| `Verification/mixed-stage-routes.json` | `7A74F2529E2DB56F371C747E575BEDC8ED207C4F75AE1A21201CAFA6998F03DE` |

No physics/preset, released catalogue, GUI, build or Git changes were made by this stage author task.
