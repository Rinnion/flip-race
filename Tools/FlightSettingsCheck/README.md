# Flight settings persistence and selection checks

Run from the project root:

```powershell
dotnet run --project Tools/FlightSettingsCheck/FlightSettingsCheck.csproj
```

This standalone check uses the production codec and selection state without Unity or external packages. It verifies all eight scale positions across the twelve action/law/repeat combinations, the two available presets (IDs 10 and 11), separate remembered impulse levels, invalid-storage fallback, custom/preset isolation, immutable active-game settings, reset, and complete copy text under English, Russian and Turkish cultures. Migration fixtures cover all ten retired active choices and custom reset targets, preserving every serialized physical field while remapping only the reset reference to 10. Retired IDs are rejected by preset selection rather than silently reused. The user's exact fast-flip payload and its inactive values are also checked.

Result on 19 October 2026: **670 assertions / 111 exact physical round trips PASS**. Browser persistence, visible selection, native clipboard and menu hit testing are separate checks. The previous 4.4 checkpoint was 594 assertions / 110 round trips; it used the now-retired preset UI and is not the current migration result.
