# 4.5.1 — one-repeat FastFlip: settings and UI integration

19 October 2026. Targeted codec/selection verification only. No runtime UI, artwork, geometry, Unity build or package was changed by this check.

```powershell
dotnet run --project Tools/FlightSettingsCheck/FlightSettingsCheck.csproj
```

**PASS: 736 assertions / 114 physical round trips.** The existing 670 checks remain, with the preset expectation updated to one repeat and the original zero-repeat payload retained as a custom regression; 66 targeted assertions were added.

- New payload `1|0|1|1|2700|35|300|530|112|10` matches preset ID11 exactly: Flip / Gravity / One, gravity 2700, flip impulse 35, inactive jump impulse 300, linear speed 530 and linear height 112. Reset target 10 remains metadata.
- Original payload `1|0|1|0|2700|35|300|530|112|10` still decodes and re-encodes unchanged, with zero repeats. The same is checked with reset target 11.
- Selecting active preset 11, returning to custom and reconstructing selection from saved data do not upgrade the remembered custom's repeat count. Active preset 11 resolves to One after reload; selecting the old custom resolves to None.
- Explicit Edit from FastFlip creates a new custom draft with One and reset target 11. Explicit Reset from target 11 loads the updated FastFlip with One. Target 10 still resets to the primary preset. Prior custom objects and serialized records are not mutated.
- As in previous versions, deliberate editing in the UI saves the new custom draft. Preservation here means no automatic custom migration or mutation when selecting/reloading a preset; it does not disable intentional editing or resetting of the user's custom draft.
- Summary/copy text report `повтор 1` / `Воздушные повторы: 1` for the new preset and zero for the old custom. Hidden physical values remain unchanged.

## UI source review

No Presentation change is needed. `FlightSettingsMenu` uses `flightSelection.Settings` for the menu summary and `Game.Flight.AirRepeats` for live hints/rules. The manual editor selects the repeat button from the draft enum. `FlightSettingsStore.Summary`, `RepeatText` and `CopyText` format the actual repeat value. The new preset therefore displays one repeat; an old custom zero-repeat set keeps its own no-repeat description.

Awake restores selection before creating the game. The active ID11 and separately serialized custom use the same existing storage keys, so this update needs no storage-version bump or custom-data rewrite.

Source hashes checked:

- Engine's updated `FlightSettings.cs`: `8499B6DC0D17A86A9CE02CE3F56FA4BD78D4F66CA1CB61E85B052C6BE5C9FB5E`
- Unchanged `FlightSettingsStore.cs`: `55DA2F7807B75DD94360CC40018F7E2C4531A841E62AC429767619A40A2982BE`
- Unchanged `FlightSettingsMenu.cs`: `FA20AC99EBB51E09F54C37D1BF039227DB336C044E980120E8421E9560CF9331`
- Updated test `Program.cs`: `314BB668072C64FCF046EE853E74EB04E2CBB0BA5047BCF49FBBC36773140D35`

Historical MODES reports are untouched. Pixel/performance and full-track design tests were not repeated. Real browser persistence and the playable second tap are verified separately by the parent/engine tasks.
