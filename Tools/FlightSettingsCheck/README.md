# Flight settings persistence and selection checks

Run from the project root:

```powershell
dotnet run --project Tools/FlightSettingsCheck/FlightSettingsCheck.csproj
```

This standalone check uses the production codec and selection state without Unity or external packages. It verifies all eight scale positions across the twelve action/law/repeat combinations, the two available presets (IDs 10 and 11), separate remembered impulse levels, invalid-storage fallback, custom/preset isolation, immutable active-game settings, reset, and complete copy text under English, Russian and Turkish cultures. Migration fixtures cover all ten retired active choices and custom reset targets, preserving every serialized physical field while remapping only the reset reference to 10. Retired IDs are rejected by preset selection rather than silently reused.

The 4.5.1 targeted cases verify that `1|0|1|1|2700|35|300|530|112|10` equals the updated FastFlip preset with one air repeat. The original `1|0|1|0|2700|35|300|530|112|10` stays a custom set with zero repeats. Active choice 11 and reload restore the new preset while preserving the separately remembered custom zero-repeat payload. Explicit Edit from FastFlip copies one repeat into a new custom draft; explicit Reset follows the stored reset source (10 → main, 11 → the updated FastFlip). Neither operation mutates the previous custom object. Normal explicit UI editing still saves the new draft, as before.

Current 4.5.1 result on 19 October 2026: **736 assertions / 114 exact physical round trips PASS**. This includes the previous 670 checks with updated preset expectations plus 66 targeted checks. Browser persistence, visible selection, native clipboard and menu hit testing are separate checks. Historical checkpoints remain 4.5.0: 670/111; 4.4: 594/110. Their recorded reports are not rewritten as 4.5.1 evidence.
