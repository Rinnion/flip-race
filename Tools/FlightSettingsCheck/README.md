# Flight settings persistence and selection checks

Run from the project root:

```powershell
dotnet run --project Tools/FlightSettingsCheck/FlightSettingsCheck.csproj
```

This standalone check uses the production codec and selection state without Unity or external packages. It verifies all eight scale positions across the twelve action/law/repeat combinations, all eleven presets including the chosen track profile, separate remembered impulse levels, invalid-storage fallback, custom/preset isolation, immutable active-game settings, reset, and complete copy text under English, Russian and Turkish cultures. It also checks the version-2 active-choice key, preserved version-1 custom key, unchanged old preset indexes, and the chosen profile's exact active and inactive values.

Result on 18 October 2026: **594 assertions / 110 exact physical round trips PASS**. Browser persistence, visible selection, native clipboard and menu hit testing are separate checks.
