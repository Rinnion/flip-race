# Flight settings persistence and selection checks

Run from the project root:

```powershell
dotnet run --project Tools/FlightSettingsCheck/FlightSettingsCheck.csproj
```

This standalone check uses the production codec and selection state without Unity or external packages. It verifies all eight scale positions across the twelve action/law/repeat combinations, exact preset values, separate remembered impulse levels, invalid-storage fallback, custom/preset isolation, immutable active-game settings, reset, and complete copy text under English, Russian and Turkish cultures.

Result on 17 October 2026: **558 assertions / 109 exact physical round trips PASS**. Browser persistence, visible selection, native clipboard and menu hit testing are separate checks.
