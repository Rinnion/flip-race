# Mixed catalogue check

21 October 2026: **PASS 2,870 checks** after production integration; see [report](../../Verification/MIX-CATALOG.md). This is a new harness based on the actual-handler method of `PostPlaytestCatalogCheck`; that historical tool and its 4.6.1 outputs are untouched.

```powershell
dotnet run --project Tools/MixedCatalogCheck/MixedCatalogCheck.csproj
```

The command verifies:

- `RaceTracks.Get(0..3)` preserves the old selections; legacy index 4/5 and invalid values still resolve to Cascade.
- `GetAt(4)` and the actual fifth card select `mixed-tricks`; the sixth slot stays inactive. String Dense/Waves remain aliases for Cascade.
- Production `FlightPointer`, `FullTrackRect`, `CreateSelectedFlight`, `BeginFlightMenu` and `Act` are invoked by reflection. Main, Fast and explicit custom None, every hero pair, ready/rematch and three viewports are checked.
- Selecting a card leaves the current round/settings/heroes intact. Apply returns ready with neither player ready. Rematch waits for two actual inputs.

This harness uses the real pure Core and compiled Presentation methods with Unity value types. It is not a rendered-browser or physical-touch test. It writes only its own `Verification/mixed-catalog-layout.json` after a successful run. The optional `--prepare` flag only compiles and prints `PREPARED` without running the assertions.
