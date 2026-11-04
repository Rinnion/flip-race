# Explicit JSON catalogue migration

From the Unity repository, run explicitly:

```powershell
dotnet run --project Tools/TrackCatalogExport/TrackCatalogExport.csproj
```

This one-time migration writes the eight current factory maps and fifteen accepted bot references to `Assets/StreamingAssets/Tracks/`, with manifest `index.json`. It refuses to overwrite authored targets and is never invoked by a normal Unity build. After migration JSON is the runtime source; editing it must not be undone by regeneration from C#.

To verify the original migration without writing JSON assets:

```powershell
dotnet run --project Tools/TrackCatalogExport/TrackCatalogExport.csproj -- --check
```

Pass an optional fresh output directory for a separate export. The tool uses shared Core `TrackJson` and compares every public stored Track field, including exact double bits, nullable overrides and original witness arrays. All fifteen reference input schedules, reward sets and B values and canonical write/read/write equality are checked. Results go to `Tools/TrackCatalogExport/Verification/migration-checks.json`. Independent catalogue integration checks own reference/Master replays.

`Samples/my-mixed-track.json` is a standalone user file with a fresh ID, existing geometry and no bot references, ready to copy/edit for session import. Schema and trust rules are in `Verification/v543-json-contract.md`. Manifest `index.json` contains relative leaf filenames, not arbitrary remote URLs. This command is an explicit migration, not a build hook or solver.
